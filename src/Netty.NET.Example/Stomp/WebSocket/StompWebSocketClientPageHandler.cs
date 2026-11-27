/*
 * Copyright 2020 The Netty Project
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
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Http.HttpHeaderNames;
using static Netty.NET.Handler.Codec.Http.HttpHeaderValues;
using static Netty.NET.Handler.Codec.Http.HttpResponseStatus;
using static Netty.NET.Handler.Codec.Http.HttpVersion;

namespace Netty.NET.Example.Stomp.WebSocket;

// CLR: @Sharable is the [Sharable] attribute, and channelRead0 is protected as the abstract
// SimpleChannelInboundHandler.ChannelRead0 it overrides (an override cannot change the accessibility). The static
// imports are using static directives; C# reports a name found in two of them as ambiguous instead of preferring the
// single-static-import of HttpHeaderValues.KEEP_ALIVE, so that constant is qualified. The Java interfaces
// FullHttpRequest, FullHttpResponse, HttpResponse and LastHttpContent are IFullHttpRequest, IFullHttpResponse,
// IHttpResponse and ILastHttpContent. INSTANCE.getClass().getResource(requestResource) resolves the name against the
// class's package, io/netty/example/stomp/websocket, in the class path; the example's resources are copied byte for
// byte to Resources/io/netty/example/stomp/websocket next to the assembly (Content items copied to the output
// directory), so GetResource returns that file's path. A name that leaves the directory, such as one with "..", or that
// names neither a file nor a directory has no resource, as getResource returns null outside the class path. new
// RandomAccessFile(file, "r") is a read-only FileStream and raf.length() its Length; opening a directory throws
// UnauthorizedAccessException where RandomAccessFile throws FileNotFoundException, so both print "File not found".
// raf.getChannel() is the FileStream that DefaultFileRegion takes, and close() is Dispose(). ChannelFutureListener.CLOSE
// is a continuation of the write Task that closes the context's channel whatever the outcome, and the other writes
// discard their Tasks as Java discards the futures. String.isEmpty() is Length == 0, startsWith/endsWith are ordinal,
// and CharsetUtil.UTF_8 is Encoding.UTF8.
[Sharable]
public sealed class StompWebSocketClientPageHandler : SimpleChannelInboundHandler<IFullHttpRequest>
{

    internal static readonly StompWebSocketClientPageHandler INSTANCE = new StompWebSocketClientPageHandler();

    private StompWebSocketClientPageHandler()
    {
    }

    protected override void ChannelRead0(IChannelHandlerContext ctx, IFullHttpRequest request)
    {
        if (request.Headers().Contains(HttpHeaderNames.UPGRADE, HttpHeaderValues.WEBSOCKET, true))
        {
            ctx.FireChannelRead(request.Retain());
            return;
        }

        if (request.DecoderResult().IsFailure())
        {
            IFullHttpResponse badRequest = new DefaultFullHttpResponse(request.ProtocolVersion(), BAD_REQUEST);
            SendResponse(badRequest, ctx, true);
            return;
        }

        if (!SendResource(request, ctx))
        {
            IFullHttpResponse notFound = new DefaultFullHttpResponse(request.ProtocolVersion(), NOT_FOUND);
            notFound.Headers().Set(CONTENT_TYPE, TEXT_PLAIN);
            string payload = "Requested resource " + request.Uri() + " not found";
            notFound.Content.WriteCharSequence(payload, Encoding.UTF8);
            HttpUtil.SetContentLength(notFound, notFound.Content.ReadableBytes);
            SendResponse(notFound, ctx, true);
        }
    }

    private static bool SendResource(IFullHttpRequest request, IChannelHandlerContext ctx)
    {
        if (request.Uri().Length == 0 || !request.Uri().StartsWith("/", StringComparison.Ordinal))
        {
            return false;
        }

        string requestResource = request.Uri().Substring(1);
        if (requestResource.Length == 0)
        {
            requestResource = "index.html";
        }

        string resourceUrl = GetResource(requestResource);
        if (resourceUrl == null)
        {
            return false;
        }

        FileStream raf = null;
        long fileLength = -1L;
        try
        {
            raf = new FileStream(resourceUrl, FileMode.Open, FileAccess.Read, FileShare.Read);
            fileLength = raf.Length;
        }
        catch (FileNotFoundException fne)
        {
            Console.WriteLine("File not found " + fne.Message);
            return false;
        }
        catch (UnauthorizedAccessException fne)
        {
            Console.WriteLine("File not found " + fne.Message);
            return false;
        }
        catch (IOException io)
        {
            Console.WriteLine("Cannot read file length " + io.Message);
            return false;
        }
        finally
        {
            if (fileLength < 0 && raf != null)
            {
                try
                {
                    raf.Dispose();
                }
                catch (IOException)
                {
                    // Nothing to do
                }
            }
        }

        IHttpResponse response = new DefaultHttpResponse(request.ProtocolVersion(), OK);
        HttpUtil.SetContentLength(response, fileLength);

        string contentType = "application/octet-stream";
        if (requestResource.EndsWith("html", StringComparison.Ordinal))
        {
            contentType = "text/html; charset=UTF-8";
        }
        else if (requestResource.EndsWith("css", StringComparison.Ordinal))
        {
            contentType = "text/css; charset=UTF-8";
        }
        else if (requestResource.EndsWith("js", StringComparison.Ordinal))
        {
            contentType = "application/javascript";
        }

        response.Headers().Set(CONTENT_TYPE, contentType);
        SendResponse(response, ctx, false);
        _ = ctx.WriteAsync(new DefaultFileRegion(raf, 0, fileLength));
        _ = ctx.WriteAndFlushAsync(ILastHttpContent.EMPTY_LAST_CONTENT);
        return true;
    }

    private static void SendResponse(IHttpResponse response, IChannelHandlerContext ctx, bool autoFlush)
    {
        if (HttpUtil.IsKeepAlive(response))
        {
            if (response.ProtocolVersion().Equals(HTTP_1_0))
            {
                response.Headers().Set(CONNECTION, HttpHeaderValues.KEEP_ALIVE);
            }
            _ = ctx.WriteAsync(response);
        }
        else
        {
            response.Headers().Set(CONNECTION, CLOSE);
            IChannel channel = ctx.Channel;
            _ = ctx.WriteAsync(response).ContinueWith(_ => channel.CloseAsync(), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, new EventExecutorTaskScheduler(() => channel.EventLoop));
        }

        if (autoFlush)
        {
            ctx.Flush();
        }
    }

    // CLR: Class.getResource(name) for the resources of this package (see the class comment).
    // System.IO.File is qualified because the Netty.NET.Example.File namespace (the file example) hides it here.
    private static string GetResource(string name)
    {
        string root = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "Resources", "io", "netty", "example", "stomp", "websocket"));
        string path;
        try
        {
            path = Path.GetFullPath(Path.Combine(root, name));
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                (!System.IO.File.Exists(path) && !Directory.Exists(path)))
        {
            return null;
        }
        return path;
    }
}
