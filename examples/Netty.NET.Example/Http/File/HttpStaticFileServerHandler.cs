/*
 * Copyright 2012 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); you may not use this file except in compliance
 * with the License. You may obtain a copy of the License at:
 *
 *   https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 */
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Handler.Ssl;
using Netty.NET.Handler.Stream;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Http.HttpMethod;
using static Netty.NET.Handler.Codec.Http.HttpResponseStatus;
using static Netty.NET.Handler.Codec.Http.HttpVersion;
using MediaTypeNames = System.Net.Mime.MediaTypeNames;

namespace Netty.NET.Example.Http.File;

/**
 * A simple handler that serves incoming HTTP requests to send their respective
 * HTTP responses.  It also implements {@code 'If-Modified-Since'} header to
 * take advantage of browser cache, as described in
 * <a href="https://tools.ietf.org/html/rfc2616#section-14.25">RFC 2616</a>.
 *
 * <h3>How Browser Caching Works</h3>
 *
 * Web browser caching works with HTTP headers as illustrated by the following
 * sample:
 * <ol>
 * <li>Request #1 returns the content of {@code /file1.txt}.</li>
 * <li>Contents of {@code /file1.txt} is cached by the browser.</li>
 * <li>Request #2 for {@code /file1.txt} does not return the contents of the
 *     file again. Rather, a 304 Not Modified is returned. This tells the
 *     browser to use the contents stored in its cache.</li>
 * <li>The server knows the file has not been modified because the
 *     {@code If-Modified-Since} date is the same as the file's last
 *     modified date.</li>
 * </ol>
 *
 * <pre>
 * Request #1 Headers
 * ===================
 * GET /file1.txt HTTP/1.1
 *
 * Response #1 Headers
 * ===================
 * HTTP/1.1 200 OK
 * Date:               Tue, 01 Mar 2011 22:44:26 GMT
 * Last-Modified:      Wed, 30 Jun 2010 21:36:48 GMT
 * Expires:            Tue, 01 Mar 2012 22:44:26 GMT
 * Cache-Control:      private, max-age=31536000
 *
 * Request #2 Headers
 * ===================
 * GET /file1.txt HTTP/1.1
 * If-Modified-Since:  Wed, 30 Jun 2010 21:36:48 GMT
 *
 * Response #2 Headers
 * ===================
 * HTTP/1.1 304 Not Modified
 * Date:               Tue, 01 Mar 2011 22:44:28 GMT
 *
 * </pre>
 */
// Directory listings omit hidden or vanished entries. Readability is checked when the file is opened. Common
// extensions map to MIME types; unknown extensions use application/octet-stream.
// HTTP dates use UTC and invariant English names. URL decoding retains malformed escapes. The user.dir override
// selects the root, which defaults to the working directory.
// Install the channel's write-progress observer only while the file is queued. Run completion and close handlers
// on the channel executor.
public class HttpStaticFileServerHandler : SimpleChannelInboundHandler<IFullHttpRequest>
{
    public const string HTTP_DATE_FORMAT = "ddd, dd MMM yyyy HH:mm:ss 'GMT'";
    public const string HTTP_DATE_GMT_TIMEZONE = "GMT";
    public const int HTTP_CACHE_SECONDS = 60;

    private IFullHttpRequest request;

    protected override void ChannelRead0(IChannelHandlerContext ctx, IFullHttpRequest request)
    {
        this.request = request;

        if (!request.DecoderResult().IsSuccess())
        {
            SendError(ctx, BAD_REQUEST);
            return;
        }

        if (!GET.Equals(request.Method()))
        {
            SendError(ctx, METHOD_NOT_ALLOWED);
            return;
        }

        bool keepAlive = HttpUtil.IsKeepAlive(request);
        string uri = request.Uri();
        string path = SanitizeUri(uri);
        if (path == null)
        {
            SendError(ctx, FORBIDDEN);
            return;
        }

        FileSystemInfo file = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        if ((file.Attributes & FileAttributes.Hidden) != 0 || !file.Exists)
        {
            SendError(ctx, NOT_FOUND);
            return;
        }

        if (file is DirectoryInfo)
        {
            if (uri.EndsWith("/", StringComparison.Ordinal))
            {
                SendListing(ctx, (DirectoryInfo) file, uri);
            }
            else
            {
                SendRedirect(ctx, uri + '/');
            }

            return;
        }

        if (!(file is FileInfo))
        {
            SendError(ctx, FORBIDDEN);
            return;
        }

        // Cache Validation
        string ifModifiedSince = request.Headers().Get(HttpHeaderNames.IF_MODIFIED_SINCE);
        if (ifModifiedSince != null && ifModifiedSince.Length != 0)
        {
            DateTimeOffset ifModifiedSinceDate = DateTimeOffset.ParseExact(
                    ifModifiedSince, HTTP_DATE_FORMAT, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

            // Only compare up to the second because the datetime format we send to the client
            // does not have milliseconds
            long ifModifiedSinceDateSeconds = ifModifiedSinceDate.ToUnixTimeMilliseconds() / 1000;
            long fileLastModifiedSeconds = new DateTimeOffset(file.LastWriteTimeUtc).ToUnixTimeMilliseconds() / 1000;
            if (ifModifiedSinceDateSeconds == fileLastModifiedSeconds)
            {
                SendNotModified(ctx);
                return;
            }
        }

        FileStream raf;

        try
        {
            raf = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (FileNotFoundException)
        {
            SendError(ctx, NOT_FOUND);
            return;
        }

        long fileLength = raf.Length;

        IHttpResponse response = new DefaultHttpResponse(HTTP_1_1, OK);
        HttpUtil.SetContentLength(response, fileLength);
        SetContentTypeHeader(response, (FileInfo) file);
        SetDateAndCacheHeaders(response, file);

        if (!keepAlive)
        {
            response.Headers().Set(HttpHeaderNames.CONNECTION, HttpHeaderValues.CLOSE);
        }
        else if (request.ProtocolVersion().Equals(HTTP_1_0))
        {
            response.Headers().Set(HttpHeaderNames.CONNECTION, HttpHeaderValues.KEEP_ALIVE);
        }

        // Write the initial line and the header.
        _ = ctx.WriteAsync(response);

        // Write the content.
        Task sendFileFuture;
        Task lastContentFuture;
        TransferProgressListener progressListener = new TransferProgressListener(ctx.Channel);
        IProgress<ChannelWriteProgress> previousObserver = ctx.Channel.Config.WriteProgress;
        ctx.Channel.Config.WriteProgress = progressListener;

        if (ctx.Pipeline.Get<SslHandler>() == null)
        {
            sendFileFuture =
                    ctx.WriteAsync(new DefaultFileRegion(raf, 0, fileLength));
            ctx.Channel.Config.WriteProgress = previousObserver;
            // Write the end marker.
            lastContentFuture = ctx.WriteAndFlushAsync(ILastHttpContent.EMPTY_LAST_CONTENT);
        }
        else
        {
            sendFileFuture =
                    ctx.WriteAsync(new HttpChunkedInput(new ChunkedFile(raf, 0, fileLength, 8192)));
            ctx.Channel.Config.WriteProgress = previousObserver;
            ctx.Flush();
            // HttpChunkedInput will write the end marker (LastHttpContent) for us.
            lastContentFuture = sendFileFuture;
        }

        _ = sendFileFuture.ContinueWith(progressListener.OperationComplete, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, new EventExecutorTaskScheduler(() => ctx.Executor));

        // Decide whether to close the connection or not.
        if (!keepAlive)
        {
            // Close the connection when the whole content is written out.
            IChannel channel = ctx.Channel;
            _ = lastContentFuture.ContinueWith(_ => channel.CloseAsync(), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, new EventExecutorTaskScheduler(() => channel.EventLoop));
        }
    }

    // CLR: Report(...) forwards each progress report of the observed write to operationProgressed; an unknown
    // total (null) is the source's negative total.
    private sealed class TransferProgressListener : IProgress<ChannelWriteProgress>
    {
        private readonly IChannel channel;

        internal TransferProgressListener(IChannel channel)
        {
            this.channel = channel;
        }

        public void OperationProgressed(ChannelWriteProgress future, long progress, long total)
        {
            if (total < 0)
            { // total unknown
                Console.Error.WriteLine(future.Channel + " Transfer progress: " + progress);
            }
            else
            {
                Console.Error.WriteLine(future.Channel + " Transfer progress: " + progress + " / " + total);
            }
        }

        public void OperationComplete(Task future)
        {
            Console.Error.WriteLine(channel + " Transfer complete.");
        }

        void IProgress<ChannelWriteProgress>.Report(ChannelWriteProgress value)
        {
            OperationProgressed(value, value.Progress.Completed, value.Progress.Total ?? -1);
        }
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        Console.Error.WriteLine(cause);

        if (ctx.Channel.IsActive)
        {
            SendError(ctx, INTERNAL_SERVER_ERROR);
        }
    }

    private static readonly Regex INSECURE_URI = new Regex("\\A(?:.*[<>&\"].*)\\z");

    private static string SanitizeUri(string uri)
    {
        // Decode the path.
        uri = System.Net.WebUtility.UrlDecode(uri);

        if (uri.Length == 0 || uri[0] != '/')
        {
            return null;
        }

        // Convert file separators.
        uri = uri.Replace('/', Path.DirectorySeparatorChar);

        // Simplistic dumb security check.
        // You will have to do something serious in the production environment.
        if (uri.Contains(Path.DirectorySeparatorChar.ToString() + '.') ||
                uri.Contains('.' + Path.DirectorySeparatorChar.ToString()) ||
                uri[0] == '.' || uri[uri.Length - 1] == '.' ||
                INSECURE_URI.IsMatch(uri))
        {
            return null;
        }

        // Convert to absolute path.
        return ExampleLauncher.GetProperty("user.dir", Directory.GetCurrentDirectory()) + Path.DirectorySeparatorChar + uri;
    }

    private static readonly Regex ALLOWED_FILE_NAME = new Regex("\\A(?:[^-\\._]?[^<>&\\\"]*)\\z");

    private void SendListing(IChannelHandlerContext ctx, DirectoryInfo dir, string dirPath)
    {
        StringBuilder buf = new StringBuilder()
                .Append("<!DOCTYPE html>\r\n")
                .Append("<html><head><meta charset='utf-8' /><title>")
                .Append("Listing of: ")
                .Append(dirPath)
                .Append("</title></head><body>\r\n")

                .Append("<h3>Listing of: ")
                .Append(dirPath)
                .Append("</h3>\r\n")

                .Append("<ul>")
                .Append("<li><a href=\"../\">..</a></li>\r\n");

        FileSystemInfo[] files = dir.GetFileSystemInfos();
        if (files != null)
        {
            foreach (FileSystemInfo f in files)
            {
                if ((f.Attributes & FileAttributes.Hidden) != 0 || !f.Exists)
                {
                    continue;
                }

                string name = f.Name;
                if (!ALLOWED_FILE_NAME.IsMatch(name))
                {
                    continue;
                }

                buf.Append("<li><a href=\"")
                        .Append(name)
                        .Append("\">")
                        .Append(name)
                        .Append("</a></li>\r\n");
            }
        }

        buf.Append("</ul></body></html>\r\n");

        ByteBuf buffer = ctx.Alloc().Buffer(buf.Length);
        buffer.WriteCharSequence(buf.ToString(), Encoding.UTF8);

        IFullHttpResponse response = new DefaultFullHttpResponse(HTTP_1_1, OK, buffer);
        response.Headers().Set(HttpHeaderNames.CONTENT_TYPE, "text/html; charset=UTF-8");

        SendAndCleanupConnection(ctx, response);
    }

    private void SendRedirect(IChannelHandlerContext ctx, string newUri)
    {
        IFullHttpResponse response = new DefaultFullHttpResponse(HTTP_1_1, FOUND, Unpooled.EmptyBuffer);
        response.Headers().Set(HttpHeaderNames.LOCATION, newUri);

        SendAndCleanupConnection(ctx, response);
    }

    private void SendError(IChannelHandlerContext ctx, HttpResponseStatus status)
    {
        IFullHttpResponse response = new DefaultFullHttpResponse(
                HTTP_1_1, status, Unpooled.CopiedBuffer("Failure: " + status + "\r\n", Encoding.UTF8));
        response.Headers().Set(HttpHeaderNames.CONTENT_TYPE, "text/plain; charset=UTF-8");

        SendAndCleanupConnection(ctx, response);
    }

    /**
     * When file timestamp is the same as what the browser is sending up, send a "304 Not Modified"
     *
     * @param ctx
     *            Context
     */
    private void SendNotModified(IChannelHandlerContext ctx)
    {
        IFullHttpResponse response = new DefaultFullHttpResponse(HTTP_1_1, NOT_MODIFIED, Unpooled.EmptyBuffer);
        SetDateHeader(response);

        SendAndCleanupConnection(ctx, response);
    }

    /**
     * If Keep-Alive is disabled, attaches "Connection: close" header to the response
     * and closes the connection after the response being sent.
     */
    private void SendAndCleanupConnection(IChannelHandlerContext ctx, IFullHttpResponse response)
    {
        IFullHttpRequest request = this.request;
        bool keepAlive = HttpUtil.IsKeepAlive(request);
        HttpUtil.SetContentLength(response, response.Content.ReadableBytes);

        if (!keepAlive)
        {
            // We're going to close the connection as soon as the response is sent,
            // so we should also make it clear for the client.
            response.Headers().Set(HttpHeaderNames.CONNECTION, HttpHeaderValues.CLOSE);
        }
        else if (request.ProtocolVersion().Equals(HTTP_1_0))
        {
            response.Headers().Set(HttpHeaderNames.CONNECTION, HttpHeaderValues.KEEP_ALIVE);
        }

        Task flushPromise = ctx.WriteAndFlushAsync(response);

        if (!keepAlive)
        {
            // Close the connection as soon as the response is sent.
            IChannel channel = ctx.Channel;
            _ = flushPromise.ContinueWith(_ => channel.CloseAsync(), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, new EventExecutorTaskScheduler(() => channel.EventLoop));
        }
    }

    /**
     * Sets the Date header for the HTTP response
     *
     * @param response
     *            HTTP response
     */
    private static void SetDateHeader(IFullHttpResponse response)
    {
        DateTime time = DateTime.UtcNow;
        response.Headers().Set(HttpHeaderNames.DATE, time.ToString(HTTP_DATE_FORMAT, CultureInfo.InvariantCulture));
    }

    /**
     * Sets the Date and Cache headers for the HTTP Response
     *
     * @param response
     *            HTTP response
     * @param fileToCache
     *            file to extract content type
     */
    private static void SetDateAndCacheHeaders(IHttpResponse response, FileSystemInfo fileToCache)
    {
        // Date header
        DateTime time = DateTime.UtcNow;
        response.Headers().Set(HttpHeaderNames.DATE, time.ToString(HTTP_DATE_FORMAT, CultureInfo.InvariantCulture));

        // Add cache headers
        time = time.AddSeconds(HTTP_CACHE_SECONDS);
        response.Headers().Set(HttpHeaderNames.EXPIRES, time.ToString(HTTP_DATE_FORMAT, CultureInfo.InvariantCulture));
        response.Headers().Set(HttpHeaderNames.CACHE_CONTROL, "private, max-age=" + HTTP_CACHE_SECONDS);
        response.Headers().Set(
                HttpHeaderNames.LAST_MODIFIED, fileToCache.LastWriteTimeUtc.ToString(HTTP_DATE_FORMAT, CultureInfo.InvariantCulture));
    }

    /**
     * Sets the content type header for the HTTP Response
     *
     * @param response
     *            HTTP response
     * @param file
     *            file to extract content type
     */
    private static void SetContentTypeHeader(IHttpResponse response, FileInfo file)
    {
        string contentType;

        switch (file.Extension.ToLowerInvariant())
        {
            case ".html":
            case ".htm":
                contentType = MediaTypeNames.Text.Html;
                break;
            case ".txt":
            case ".text":
                contentType = MediaTypeNames.Text.Plain;
                break;
            case ".css":
                contentType = MediaTypeNames.Text.Css;
                break;
            case ".js":
                contentType = MediaTypeNames.Text.JavaScript;
                break;
            case ".xml":
                contentType = MediaTypeNames.Text.Xml;
                break;
            case ".json":
                contentType = MediaTypeNames.Application.Json;
                break;
            case ".gif":
                contentType = MediaTypeNames.Image.Gif;
                break;
            case ".jpeg":
            case ".jpg":
            case ".jpe":
                contentType = MediaTypeNames.Image.Jpeg;
                break;
            case ".png":
                contentType = MediaTypeNames.Image.Png;
                break;
            case ".svg":
                contentType = MediaTypeNames.Image.Svg;
                break;
            case ".ico":
                contentType = MediaTypeNames.Image.Icon;
                break;
            case ".tiff":
            case ".tif":
                contentType = MediaTypeNames.Image.Tiff;
                break;
            case ".pdf":
                contentType = MediaTypeNames.Application.Pdf;
                break;
            case ".rtf":
                contentType = MediaTypeNames.Application.Rtf;
                break;
            case ".zip":
                contentType = MediaTypeNames.Application.Zip;
                break;
            default:
                contentType = MediaTypeNames.Application.Octet;
                break;
        }

        response.Headers().Set(HttpHeaderNames.CONTENT_TYPE, contentType);
    }
}
