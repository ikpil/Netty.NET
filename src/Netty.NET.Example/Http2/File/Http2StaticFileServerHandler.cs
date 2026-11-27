/*
 * Copyright 2022 The Netty Project
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
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Handler.Codec.Http2;
using Netty.NET.Handler.Stream;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Http.HttpMethod;
using static Netty.NET.Handler.Codec.Http.HttpResponseStatus;

namespace Netty.NET.Example.Http2.File;

/**
 * A simple handler that serves incoming HTTP requests to send their respective
 * HTTP responses.  It also implements {@code 'If-Modified-Since'} header to
 * take advantage of browser cache, as described in
 * <a href="https://tools.ietf.org/html/rfc2616#section-14.25">RFC 2616</a>.
 *
 * <h3>How Browser Caching Works</h3>
 * <p>
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
// CLR: java.io.File names a file or a directory; it is a DirectoryInfo when a directory of that path exists and a FileInfo
// otherwise (FileInfo.Exists is false for a directory), so isDirectory()/isFile() are the FileSystemInfo's type, exists()
// is Exists, isHidden() the Hidden attribute (which .NET also reports for dot files on Unix), canRead() whether the
// entry's attributes can be read, getName() Name and lastModified() the Unix time of LastWriteTimeUtc in milliseconds
// (System.IO.File is not named: the Netty.NET.Example.File namespace hides it). RandomAccessFile(file, "r") is a read-only
// FileStream; Java reports a missing or unreadable file as FileNotFoundException, .NET as FileNotFoundException or
// UnauthorizedAccessException. SimpleDateFormat(HTTP_DATE_FORMAT, Locale.US) with the GMT time zone
// (HTTP_DATE_GMT_TIMEZONE) is the .NET custom format of the same text, HTTP_DATE_FORMAT, applied to UTC times with the
// invariant culture (English names; .NET has no time zone name specifier, so the zone is the literal GMT); parse is
// DateTime.ParseExact, whose FormatException reaches exceptionCaught as Java's ParseException does. GregorianCalendar
// and Date are the current UTC DateTime. URLDecoder.decode(uri, "UTF-8") is WebUtility.UrlDecode ("+" is a space, %xx
// UTF-8 escapes) and SystemPropertyUtil.get("user.dir") reads the user.dir property with the process's current directory
// as its default (the JVM sets user.dir to the working directory; ExampleLauncher.GetProperty reads only -Dname=value arguments).
// Pattern.matcher(s).matches() is a whole-string match of the Regex anchored with \A and \z; Java's "." excludes the
// line terminators \n, \r, U+0085, U+2028 and U+2029, so it is the class of every other character (JAVA_DOT), as
// handler's SslHandler maps it. javax.activation.MimetypesFileTypeMap (the JavaBeans Activation Framework that the example's pom adds,
// com.sun.activation:javax.activation) has no BCL counterpart; getContentType(path) is the CLR-only MIME_TYPES table of
// common file name extensions, with "application/octet-stream", MimetypesFileTypeMap's answer for an unknown extension,
// as the default. Http2Headers, Http2HeadersFrame, Http2DataFrame and Http2FrameStream take the I prefix; the
// CharSequence values of the headers are Common's StringCharSequence (docs/codec-http-porting.md) and
// HttpResponseStatus.toString() keeps Java's "code reasonPhrase" text. ctx.write/writeAndFlush are WriteAsync/
// WriteAndFlushAsync, whose Tasks are discarded as Java discards the futures. ctx.newProgressivePromise() and the
// ChannelProgressiveFutureListener are the channel's write-progress observer (a ChannelProgressivePromise is
// IChannelConfig.WriteProgress, docs/transport-porting.md), which ChunkedWriteHandler captures for the queued write, and
// a continuation of the write Task (see SendFileProgressListener). cause.printStackTrace() writes the exception to
// Console.Error and System.out.println/System.err.println are Console.WriteLine/Console.Error.WriteLine.
public class Http2StaticFileServerHandler : ChannelDuplexHandler
{

    public const string HTTP_DATE_FORMAT = "ddd, dd MMM yyyy HH:mm:ss 'GMT'";
    public const string HTTP_DATE_GMT_TIMEZONE = "GMT";
    public const int HTTP_CACHE_SECONDS = 60;

    // CLR-only: the content types of MimetypesFileTypeMap.getContentType for common extensions (see the class comment).
    private static readonly Dictionary<string, string> MIME_TYPES = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { ".html", "text/html" }, { ".htm", "text/html" }, { ".txt", "text/plain" }, { ".text", "text/plain" },
        { ".java", "text/plain" }, { ".gif", "image/gif" }, { ".ief", "image/ief" }, { ".jpeg", "image/jpeg" },
        { ".jpg", "image/jpeg" }, { ".jpe", "image/jpeg" }, { ".png", "image/png" }, { ".tiff", "image/tiff" },
        { ".tif", "image/tiff" }, { ".ps", "application/postscript" }, { ".eps", "application/postscript" },
        { ".ai", "application/postscript" }, { ".rtf", "application/rtf" }, { ".tex", "application/x-tex" },
        { ".au", "audio/basic" }, { ".mid", "audio/midi" }, { ".midi", "audio/midi" }, { ".aif", "audio/x-aiff" },
        { ".aiff", "audio/x-aiff" }, { ".wav", "audio/x-wav" }, { ".mpeg", "video/mpeg" }, { ".mpg", "video/mpeg" },
        { ".mpe", "video/mpeg" }, { ".qt", "video/quicktime" }, { ".mov", "video/quicktime" },
        { ".avi", "video/x-msvideo" },
    };

    // CLR-only: Java's "." in Pattern, every character but the line terminators.
    private const string JAVA_DOT = "[^\\n\\r\\u0085\\u2028\\u2029]";

    private IHttp2FrameStream stream;

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        if (msg is IHttp2HeadersFrame)
        {
            IHttp2HeadersFrame headersFrame = (IHttp2HeadersFrame) msg;
            this.stream = headersFrame.Stream();

            if (!GET.ToString().Equals(headersFrame.Headers().Method().ToString()))
            {
                SendError(ctx, METHOD_NOT_ALLOWED);
                return;
            }

            string uri = headersFrame.Headers().Path().ToString();
            string path = SanitizeUri(uri);
            if (path == null)
            {
                SendError(ctx, FORBIDDEN);
                return;
            }

            FileSystemInfo file = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
            if (IsHidden(file) || !file.Exists)
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
            ICharSequence ifModifiedSince = headersFrame.Headers().Get(HttpHeaderNames.IF_MODIFIED_SINCE);
            if (ifModifiedSince != null && ifModifiedSince.ToString().Length != 0)
            {
                DateTime ifModifiedSinceDate = DateTime.ParseExact(ifModifiedSince.ToString(), HTTP_DATE_FORMAT,
                        CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

                // Only compare up to the second because the datetime format we send to the client
                // does not have milliseconds
                long ifModifiedSinceDateSeconds = new DateTimeOffset(ifModifiedSinceDate).ToUnixTimeMilliseconds() / 1000;
                long fileLastModifiedSeconds = LastModified(file) / 1000;
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
            catch (Exception ignore) when (ignore is FileNotFoundException || ignore is UnauthorizedAccessException)
            {
                SendError(ctx, NOT_FOUND);
                return;
            }
            long fileLength = raf.Length;

            IHttp2Headers headers = new DefaultHttp2Headers();
            headers.Status(new StringCharSequence("200"));
            headers.SetLong(HttpHeaderNames.CONTENT_LENGTH, fileLength);

            SetContentTypeHeader(headers, file);
            SetDateAndCacheHeaders(headers, file);

            // Write the initial line and the header.
            _ = ctx.WriteAndFlushAsync(new DefaultHttp2HeadersFrame(headers).Stream(stream));

            // Write the content.
            Task sendFileFuture;
            IChannelConfig config = ctx.Channel.Config;
            IProgress<ChannelWriteProgress> previousProgress = config.WriteProgress;
            SendFileProgressListener listener = new SendFileProgressListener(ctx.Channel);
            config.WriteProgress = listener;
            try
            {
                sendFileFuture = ctx.WriteAndFlushAsync(new Http2DataChunkedInput(
                        new ChunkedFile(raf, 0, fileLength, 8192), stream));
            }
            finally
            {
                config.WriteProgress = previousProgress;
            }

            listener.AddTo(sendFileFuture, ctx);
        }
        else
        {
            // Unsupported message type
            Console.WriteLine("Unsupported message type: " + msg);
        }
    }

    // CLR: the anonymous ChannelProgressiveFutureListener. It is the channel's write-progress observer while the file is
    // queued (ChunkedWriteHandler captures the observer of the channel for its pending write) and reports only the
    // progress of the send-file Task; AddTo is addListener: Java adds the listener after writeAndFlush returned, so the
    // progress of chunks written before is not reported to it (the Task is unknown until then), and operationComplete
    // is a continuation of the Task on the channel's executor (Common's EventExecutorTaskScheduler, the timing of
    // DefaultPromise listeners). future.channel() is the channel the listener was created for; a total below zero is an
    // unknown TransferProgress.Total (null).
    private sealed class SendFileProgressListener : IProgress<ChannelWriteProgress>
    {
        private readonly IChannel channel;
        private volatile Task future;

        internal SendFileProgressListener(IChannel channel)
        {
            this.channel = channel;
        }

        internal void AddTo(Task future, IChannelHandlerContext ctx)
        {
            this.future = future;
            _ = future.ContinueWith(OperationComplete, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                    new EventExecutorTaskScheduler(() => ctx.Executor));
        }

        public void Report(ChannelWriteProgress value)
        {
            Task future = this.future;
            if (future != null && ReferenceEquals(value.Operation, future))
            {
                OperationProgressed(value.Progress.Completed, value.Progress.Total);
            }
        }

        private void OperationProgressed(long progress, long? total)
        {
            if (total == null)
            { // total unknown
                Console.Error.WriteLine(channel + " Transfer progress: " + progress);
            }
            else
            {
                Console.Error.WriteLine(channel + " Transfer progress: " + progress + " / " + total);
            }
        }

        private void OperationComplete(Task future)
        {
            Console.Error.WriteLine(channel + " Transfer complete.");
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

    private static readonly Regex INSECURE_URI = new Regex("\\A(?:" + JAVA_DOT + "*[<>&\"]" + JAVA_DOT + "*)\\z");

    private static string SanitizeUri(string uri)
    {
        // Decode the path.
        uri = WebUtility.UrlDecode(uri);

        if (uri.Length == 0 || uri[0] != '/')
        {
            return null;
        }

        // Convert file separators.
        uri = uri.Replace('/', Path.DirectorySeparatorChar);

        // Simplistic dumb security check.
        // You will have to do something serious in the production environment.
        string separator = Path.DirectorySeparatorChar.ToString();
        if (uri.Contains(separator + '.') ||
                uri.Contains('.' + separator) ||
                uri[0] == '.' || uri[uri.Length - 1] == '.' ||
                INSECURE_URI.IsMatch(uri))
        {
            return null;
        }

        // Convert to absolute path.
        return ExampleLauncher.GetProperty("user.dir", Environment.CurrentDirectory) + separator + uri;
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
                if (IsHidden(f) || !CanRead(f))
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

        IHttp2Headers headers = new DefaultHttp2Headers();
        headers.Status(new StringCharSequence(OK.ToString()));
        headers.Add(HttpHeaderNames.CONTENT_TYPE, new StringCharSequence("text/html; charset=UTF-8"));

        _ = ctx.WriteAsync(new DefaultHttp2HeadersFrame(headers).Stream(stream));
        _ = ctx.WriteAndFlushAsync(new DefaultHttp2DataFrame(buffer, true).Stream(stream));
    }

    private void SendRedirect(IChannelHandlerContext ctx, string newUri)
    {
        IHttp2Headers headers = new DefaultHttp2Headers();
        headers.Status(new StringCharSequence(FOUND.ToString()));
        headers.Add(HttpHeaderNames.LOCATION, new StringCharSequence(newUri));

        _ = ctx.WriteAndFlushAsync(new DefaultHttp2HeadersFrame(headers, true).Stream(stream));
    }

    private void SendError(IChannelHandlerContext ctx, HttpResponseStatus status)
    {
        IHttp2Headers headers = new DefaultHttp2Headers();
        headers.Status(new StringCharSequence(status.ToString()));
        headers.Add(HttpHeaderNames.CONTENT_TYPE, new StringCharSequence("text/plain; charset=UTF-8"));

        IHttp2HeadersFrame headersFrame = new DefaultHttp2HeadersFrame(headers);
        headersFrame.Stream(stream);

        IHttp2DataFrame dataFrame = new DefaultHttp2DataFrame(
                Unpooled.CopiedBuffer("Failure: " + status + "\r\n", Encoding.UTF8), true);
        dataFrame.Stream(stream);

        _ = ctx.WriteAsync(headersFrame);
        _ = ctx.WriteAndFlushAsync(dataFrame);
    }

    /**
     * When file timestamp is the same as what the browser is sending up, send a "304 Not Modified"
     *
     * @param ctx Context
     */
    private void SendNotModified(IChannelHandlerContext ctx)
    {
        IHttp2Headers headers = new DefaultHttp2Headers();
        headers.Status(new StringCharSequence(NOT_MODIFIED.ToString()));
        SetDateHeader(headers);

        _ = ctx.WriteAndFlushAsync(new DefaultHttp2HeadersFrame(headers, true).Stream(stream));
    }

    /**
     * Sets the Date header for the HTTP response
     *
     * @param headers Http2 Headers
     */
    private static void SetDateHeader(IHttp2Headers headers)
    {
        DateTime time = DateTime.UtcNow;
        headers.Set(HttpHeaderNames.DATE, new StringCharSequence(
                time.ToString(HTTP_DATE_FORMAT, CultureInfo.InvariantCulture)));
    }

    /**
     * Sets the Date and Cache headers for the HTTP Response
     *
     * @param headers     Http2 Headers
     * @param fileToCache file to extract content type
     */
    private static void SetDateAndCacheHeaders(IHttp2Headers headers, FileSystemInfo fileToCache)
    {
        // Date header
        DateTime time = DateTime.UtcNow;
        headers.Set(HttpHeaderNames.DATE, new StringCharSequence(
                time.ToString(HTTP_DATE_FORMAT, CultureInfo.InvariantCulture)));

        // Add cache headers
        time = time.AddSeconds(HTTP_CACHE_SECONDS);
        headers.Set(HttpHeaderNames.EXPIRES, new StringCharSequence(
                time.ToString(HTTP_DATE_FORMAT, CultureInfo.InvariantCulture)));
        headers.Set(HttpHeaderNames.CACHE_CONTROL, new StringCharSequence("private, max-age=" + HTTP_CACHE_SECONDS));
        headers.Set(HttpHeaderNames.LAST_MODIFIED, new StringCharSequence(
                DateTimeOffset.FromUnixTimeMilliseconds(LastModified(fileToCache)).UtcDateTime
                        .ToString(HTTP_DATE_FORMAT, CultureInfo.InvariantCulture)));
    }

    /**
     * Sets the content type header for the HTTP Response
     *
     * @param headers Http2 Headers
     * @param file    file to extract content type
     */
    private static void SetContentTypeHeader(IHttp2Headers headers, FileSystemInfo file)
    {
        string contentType = MIME_TYPES.TryGetValue(Path.GetExtension(file.FullName), out string type) ? type
                : "application/octet-stream";
        headers.Set(HttpHeaderNames.CONTENT_TYPE, new StringCharSequence(contentType));
    }

    // CLR-only: File.isHidden().
    private static bool IsHidden(FileSystemInfo file)
    {
        return file.Exists && (file.Attributes & FileAttributes.Hidden) != 0;
    }

    // CLR-only: File.canRead(), whether the entry's attributes can be read.
    private static bool CanRead(FileSystemInfo file)
    {
        try
        {
            _ = file.Attributes;
            return true;
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            return false;
        }
    }

    // CLR-only: File.lastModified(), the milliseconds since the epoch of the last write.
    private static long LastModified(FileSystemInfo file)
    {
        return new DateTimeOffset(file.LastWriteTimeUtc).ToUnixTimeMilliseconds();
    }
}
