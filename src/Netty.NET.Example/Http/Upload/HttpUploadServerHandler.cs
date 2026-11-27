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
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Handler.Codec;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Handler.Codec.Http.Cookie;
using Netty.NET.Handler.Codec.Http.Multipart;
using Netty.NET.Transport.Channels;
using static Netty.NET.Buffer.Unpooled;
using EndOfDataDecoderException = Netty.NET.Handler.Codec.Http.Multipart.HttpPostRequestDecoder.EndOfDataDecoderException;
using ErrorDataDecoderException = Netty.NET.Handler.Codec.Http.Multipart.HttpPostRequestDecoder.ErrorDataDecoderException;
using HttpDataType = Netty.NET.Handler.Codec.Http.Multipart.IInterfaceHttpData.HttpDataType;

namespace Netty.NET.Example.Http.Upload;

// CLR adaptations:
// - java.util.logging's Logger.getLogger(...) is Common's InternalLoggerFactory logger; logger.info is Info and
//   logger.log(Level.WARNING, msg, cause) is Warn(msg, cause).
// - The static initializer is the static constructor; DiskFileUpload/DiskAttribute keep their public static fields
//   (baseDirectory, deleteOnExitTemporaryFile) under the Java names.
// - new URI(request.uri()).getPath() is the decoded path (Uri.UnescapeDataString of AbsolutePath) of a System.Uri. A
//   Uri gives no path for a relative reference such as the origin-form "/formpost?x=1", so the request target is
//   resolved against a placeholder origin (http://localhost/); an absolute-form target, which HttpUploadClient sends,
//   keeps its own. A malformed target throws UriFormatException where Java throws URISyntaxException.
// - HttpRequest/HttpObject/HttpContent/LastHttpContent/FullHttpResponse, Attribute, FileUpload, HttpData,
//   HttpDataFactory and InterfaceHttpData are the I-prefixed codec-http interfaces; Cookie is ICookie, Set<Cookie> is
//   ISet<ICookie> and Collections.emptySet() is ImmutableHashSet<ICookie>.Empty. The nested HttpPostRequestDecoder
//   exceptions and InterfaceHttpData.HttpDataType are named by using aliases, as the Java imports name them, and
//   HttpDataType.name() is the enum's ToString(). Map.Entry<String, String> of the headers is IEntry<string, string> and
//   Map<String, List<String>> is IDictionary<string, IList<string>>.
// - Java string concatenation writes a boolean as "true"/"false"; C# bool.ToString() is "True"/"False", so the two
//   booleans are written with the Java spelling. StringBuilder.setLength(0) is Clear(), copiedBuffer(...,
//   CharsetUtil.UTF_8) is CopiedBuffer(..., Encoding.UTF8) and IOException is System.IO.IOException.
// - readHttpDataChunkByChunk declares a second local named data after the loop whose body declares one; C# forbids a
//   local that reuses the name of a nested scope's local (CS0136), so the second one is currentData.
// - channel.writeAndFlush returns a Task (ChannelFuture); ChannelFutureListener.CLOSE is a synchronous Task continuation
//   that closes the channel on its event loop (Common's EventExecutorTaskScheduler), as codec-http's CorsHandler maps
//   it. printStackTrace() writes the exception to Console.Error and ctx.channel().close() is CloseAsync(), whose Task is
//   discarded as Java discards the future.
public class HttpUploadServerHandler : SimpleChannelInboundHandler<IHttpObject>
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(HttpUploadServerHandler));

    private IHttpRequest request;

    private IHttpData partialContent;

    private readonly StringBuilder responseContent = new StringBuilder();

    private static readonly IHttpDataFactory factory =
            new DefaultHttpDataFactory(DefaultHttpDataFactory.MINSIZE); // Disk if size exceed

    private HttpPostRequestDecoder decoder;

    static HttpUploadServerHandler()
    {
        DiskFileUpload.deleteOnExitTemporaryFile = true; // should delete file
                                                         // on exit (in normal
                                                         // exit)
        DiskFileUpload.baseDirectory = null; // system temp directory
        DiskAttribute.deleteOnExitTemporaryFile = true; // should delete file on
                                                        // exit (in normal exit)
        DiskAttribute.baseDirectory = null; // system temp directory
    }

    public override void ChannelInactive(IChannelHandlerContext ctx)
    {
        if (decoder != null)
        {
            decoder.CleanFiles();
        }
    }

    protected override void ChannelRead0(IChannelHandlerContext ctx, IHttpObject msg)
    {
        if (msg is IHttpRequest)
        {
            IHttpRequest request = this.request = (IHttpRequest) msg;
            Uri uri = new Uri(new Uri("http://localhost/"), request.Uri());
            if (!Uri.UnescapeDataString(uri.AbsolutePath).StartsWith("/form", StringComparison.Ordinal))
            {
                // Write Menu
                WriteMenu(ctx);
                return;
            }
            responseContent.Clear();
            responseContent.Append("WELCOME TO THE WILD WILD WEB SERVER\r\n");
            responseContent.Append("===================================\r\n");

            responseContent.Append("VERSION: " + request.ProtocolVersion().Text() + "\r\n");

            responseContent.Append("REQUEST_URI: " + request.Uri() + "\r\n\r\n");
            responseContent.Append("\r\n\r\n");

            // new getMethod
            foreach (IEntry<string, string> entry in request.Headers())
            {
                responseContent.Append("HEADER: " + entry.GetKey() + '=' + entry.GetValue() + "\r\n");
            }
            responseContent.Append("\r\n\r\n");

            // new getMethod
            ISet<ICookie> cookies;
            string value = request.Headers().Get(HttpHeaderNames.COOKIE);
            if (value == null)
            {
                cookies = ImmutableHashSet<ICookie>.Empty;
            }
            else
            {
                cookies = ServerCookieDecoder.STRICT.Decode(value);
            }
            foreach (ICookie cookie in cookies)
            {
                responseContent.Append("COOKIE: " + cookie + "\r\n");
            }
            responseContent.Append("\r\n\r\n");

            QueryStringDecoder decoderQuery = new QueryStringDecoder(request.Uri());
            IDictionary<string, IList<string>> uriAttributes = decoderQuery.Parameters();
            foreach (KeyValuePair<string, IList<string>> attr in uriAttributes)
            {
                foreach (string attrVal in attr.Value)
                {
                    responseContent.Append("URI: " + attr.Key + '=' + attrVal + "\r\n");
                }
            }
            responseContent.Append("\r\n\r\n");

            // if GET Method: should not try to create an HttpPostRequestDecoder
            if (HttpMethod.GET.Equals(request.Method()))
            {
                // GET Method: should not try to create an HttpPostRequestDecoder
                // So stop here
                responseContent.Append("\r\n\r\nEND OF GET CONTENT\r\n");
                // Not now: LastHttpContent will be sent writeResponse(ctx.channel());
                return;
            }
            try
            {
                decoder = new HttpPostRequestDecoder(factory, request);
            }
            catch (ErrorDataDecoderException e1)
            {
                Console.Error.WriteLine(e1);
                responseContent.Append(e1.Message);
                WriteResponse(ctx.Channel, true);
                return;
            }

            bool readingChunks = HttpUtil.IsTransferEncodingChunked(request);
            responseContent.Append("Is Chunked: " + (readingChunks ? "true" : "false") + "\r\n");
            responseContent.Append("IsMultipart: " + (decoder.IsMultipart() ? "true" : "false") + "\r\n");
            if (readingChunks)
            {
                // Chunk version
                responseContent.Append("Chunks: ");
            }
        }

        // check if the decoder was constructed before
        // if not it handles the form get
        if (decoder != null)
        {
            if (msg is IHttpContent)
            {
                // New chunk is received
                IHttpContent chunk = (IHttpContent) msg;
                try
                {
                    decoder.Offer(chunk);
                }
                catch (ErrorDataDecoderException e1)
                {
                    Console.Error.WriteLine(e1);
                    responseContent.Append(e1.Message);
                    WriteResponse(ctx.Channel, true);
                    return;
                }
                responseContent.Append('o');
                // example of reading chunk by chunk (minimize memory usage due to
                // Factory)
                ReadHttpDataChunkByChunk();
                // example of reading only if at the end
                if (chunk is ILastHttpContent)
                {
                    WriteResponse(ctx.Channel);

                    Reset();
                }
            }
        }
        else
        {
            WriteResponse(ctx.Channel);
        }
    }

    private void Reset()
    {
        request = null;

        // destroy the decoder to release all resources
        decoder.Destroy();
        decoder = null;
    }

    /**
     * Example of reading request by chunk and getting values from chunk to chunk
     */
    private void ReadHttpDataChunkByChunk()
    {
        try
        {
            while (decoder.HasNext())
            {
                IInterfaceHttpData data = decoder.Next();
                if (data != null)
                {
                    // check if current HttpData is a FileUpload and previously set as partial
                    if (partialContent == data)
                    {
                        logger.Info(" 100% (FinalSize: " + partialContent.Length() + ")");
                        partialContent = null;
                    }
                    // new value
                    WriteHttpData(data);
                }
            }
            // Check partial decoding for a FileUpload
            IInterfaceHttpData currentData = decoder.CurrentPartialHttpData();
            if (currentData != null)
            {
                StringBuilder builder = new StringBuilder();
                if (partialContent == null)
                {
                    partialContent = (IHttpData) currentData;
                    if (partialContent is IFileUpload)
                    {
                        builder.Append("Start FileUpload: ")
                            .Append(((IFileUpload) partialContent).GetFilename()).Append(" ");
                    }
                    else
                    {
                        builder.Append("Start Attribute: ")
                            .Append(partialContent.GetName()).Append(" ");
                    }
                    builder.Append("(DefinedSize: ").Append(partialContent.DefinedLength()).Append(")");
                }
                if (partialContent.DefinedLength() > 0)
                {
                    builder.Append(" ").Append(partialContent.Length() * 100 / partialContent.DefinedLength())
                        .Append("% ");
                    logger.Info(builder.ToString());
                }
                else
                {
                    builder.Append(" ").Append(partialContent.Length()).Append(" ");
                    logger.Info(builder.ToString());
                }
            }
        }
        catch (EndOfDataDecoderException)
        {
            // end
            responseContent.Append("\r\n\r\nEND OF CONTENT CHUNK BY CHUNK\r\n\r\n");
        }
    }

    private void WriteHttpData(IInterfaceHttpData data)
    {
        if (data.GetHttpDataType() == HttpDataType.Attribute)
        {
            IAttribute attribute = (IAttribute) data;
            string value;
            try
            {
                value = attribute.GetValue();
            }
            catch (IOException e1)
            {
                // Error while reading data from File, only print name and error
                Console.Error.WriteLine(e1);
                responseContent.Append("\r\nBODY Attribute: " + attribute.GetHttpDataType() + ": "
                        + attribute.GetName() + " Error while reading value: " + e1.Message + "\r\n");
                return;
            }
            if (value.Length > 100)
            {
                responseContent.Append("\r\nBODY Attribute: " + attribute.GetHttpDataType() + ": "
                        + attribute.GetName() + " data too long\r\n");
            }
            else
            {
                responseContent.Append("\r\nBODY Attribute: " + attribute.GetHttpDataType() + ": "
                        + attribute + "\r\n");
            }
        }
        else
        {
            responseContent.Append("\r\nBODY FileUpload: " + data.GetHttpDataType() + ": " + data
                    + "\r\n");
            if (data.GetHttpDataType() == HttpDataType.FileUpload)
            {
                IFileUpload fileUpload = (IFileUpload) data;
                if (fileUpload.IsCompleted())
                {
                    if (fileUpload.Length() < 10000)
                    {
                        responseContent.Append("\tContent of file\r\n");
                        try
                        {
                            responseContent.Append(fileUpload.GetString(fileUpload.GetCharset()));
                        }
                        catch (IOException e1)
                        {
                            // do nothing for the example
                            Console.Error.WriteLine(e1);
                        }
                        responseContent.Append("\r\n");
                    }
                    else
                    {
                        responseContent.Append("\tFile too long to be printed out:" + fileUpload.Length() + "\r\n");
                    }
                    // fileUpload.isInMemory();// tells if the file is in Memory
                    // or on File
                    // fileUpload.renameTo(dest); // enable to move into another
                    // File dest
                    // decoder.removeFileUploadFromClean(fileUpload); //remove
                    // the File of to delete file
                }
                else
                {
                    responseContent.Append("\tFile to be continued but should not!\r\n");
                }
            }
        }
    }

    private void WriteResponse(IChannel channel)
    {
        WriteResponse(channel, false);
    }

    private void WriteResponse(IChannel channel, bool forceClose)
    {
        // Convert the response content to a ChannelBuffer.
        ByteBuf buf = CopiedBuffer(responseContent.ToString(), Encoding.UTF8);
        responseContent.Clear();

        // Decide whether to close the connection or not.
        bool keepAlive = HttpUtil.IsKeepAlive(request) && !forceClose;

        // Build the response object.
        IFullHttpResponse response = new DefaultFullHttpResponse(
                HttpVersion.HTTP_1_1, HttpResponseStatus.OK, buf);
        response.Headers().Set(HttpHeaderNames.CONTENT_TYPE, "text/plain; charset=UTF-8");
        response.Headers().SetInt(HttpHeaderNames.CONTENT_LENGTH, buf.ReadableBytes);

        if (!keepAlive)
        {
            response.Headers().Set(HttpHeaderNames.CONNECTION, HttpHeaderValues.CLOSE);
        }
        else if (request.ProtocolVersion().Equals(HttpVersion.HTTP_1_0))
        {
            response.Headers().Set(HttpHeaderNames.CONNECTION, HttpHeaderValues.KEEP_ALIVE);
        }

        ISet<ICookie> cookies;
        string value = request.Headers().Get(HttpHeaderNames.COOKIE);
        if (value == null)
        {
            cookies = ImmutableHashSet<ICookie>.Empty;
        }
        else
        {
            cookies = ServerCookieDecoder.STRICT.Decode(value);
        }
        if (cookies.Count != 0)
        {
            // Reset the cookies if necessary.
            foreach (ICookie cookie in cookies)
            {
                response.Headers().Add(HttpHeaderNames.SET_COOKIE, ServerCookieEncoder.STRICT.Encode(cookie));
            }
        }
        // Write the response.
        Task future = channel.WriteAndFlushAsync(response);
        // Close the connection after the write operation is done if necessary.
        if (!keepAlive)
        {
            _ = future.ContinueWith(_ => channel.CloseAsync(), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, new EventExecutorTaskScheduler(() => channel.EventLoop));
        }
    }

    private void WriteMenu(IChannelHandlerContext ctx)
    {
        // print several HTML forms
        // Convert the response content to a ChannelBuffer.
        responseContent.Clear();

        // create Pseudo Menu
        responseContent.Append("<html>");
        responseContent.Append("<head>");
        responseContent.Append("<title>Netty Test Form</title>\r\n");
        responseContent.Append("</head>\r\n");
        responseContent.Append("<body bgcolor=white><style>td{font-size: 12pt;}</style>");

        responseContent.Append("<table border=\"0\">");
        responseContent.Append("<tr>");
        responseContent.Append("<td>");
        responseContent.Append("<h1>Netty Test Form</h1>");
        responseContent.Append("Choose one FORM");
        responseContent.Append("</td>");
        responseContent.Append("</tr>");
        responseContent.Append("</table>\r\n");

        // GET
        responseContent.Append("<CENTER>GET FORM<HR WIDTH=\"75%\" NOSHADE color=\"blue\"></CENTER>");
        responseContent.Append("<FORM ACTION=\"/formget\" METHOD=\"GET\">");
        responseContent.Append("<input type=hidden name=getform value=\"GET\">");
        responseContent.Append("<table border=\"0\">");
        responseContent.Append("<tr><td>Fill with value: <br> <input type=text name=\"info\" size=10></td></tr>");
        responseContent.Append("<tr><td>Fill with value: <br> <input type=text name=\"secondinfo\" size=20>");
        responseContent
                .Append("<tr><td>Fill with value: <br> <textarea name=\"thirdinfo\" cols=40 rows=10></textarea>");
        responseContent.Append("</td></tr>");
        responseContent.Append("<tr><td><INPUT TYPE=\"submit\" NAME=\"Send\" VALUE=\"Send\"></INPUT></td>");
        responseContent.Append("<td><INPUT TYPE=\"reset\" NAME=\"Clear\" VALUE=\"Clear\" ></INPUT></td></tr>");
        responseContent.Append("</table></FORM>\r\n");
        responseContent.Append("<CENTER><HR WIDTH=\"75%\" NOSHADE color=\"blue\"></CENTER>");

        // POST
        responseContent.Append("<CENTER>POST FORM<HR WIDTH=\"75%\" NOSHADE color=\"blue\"></CENTER>");
        responseContent.Append("<FORM ACTION=\"/formpost\" METHOD=\"POST\">");
        responseContent.Append("<input type=hidden name=getform value=\"POST\">");
        responseContent.Append("<table border=\"0\">");
        responseContent.Append("<tr><td>Fill with value: <br> <input type=text name=\"info\" size=10></td></tr>");
        responseContent.Append("<tr><td>Fill with value: <br> <input type=text name=\"secondinfo\" size=20>");
        responseContent
                .Append("<tr><td>Fill with value: <br> <textarea name=\"thirdinfo\" cols=40 rows=10></textarea>");
        responseContent.Append("<tr><td>Fill with file (only file name will be transmitted): <br> "
                + "<input type=file name=\"myfile\">");
        responseContent.Append("</td></tr>");
        responseContent.Append("<tr><td><INPUT TYPE=\"submit\" NAME=\"Send\" VALUE=\"Send\"></INPUT></td>");
        responseContent.Append("<td><INPUT TYPE=\"reset\" NAME=\"Clear\" VALUE=\"Clear\" ></INPUT></td></tr>");
        responseContent.Append("</table></FORM>\r\n");
        responseContent.Append("<CENTER><HR WIDTH=\"75%\" NOSHADE color=\"blue\"></CENTER>");

        // POST with enctype="multipart/form-data"
        responseContent.Append("<CENTER>POST MULTIPART FORM<HR WIDTH=\"75%\" NOSHADE color=\"blue\"></CENTER>");
        responseContent.Append("<FORM ACTION=\"/formpostmultipart\" ENCTYPE=\"multipart/form-data\" METHOD=\"POST\">");
        responseContent.Append("<input type=hidden name=getform value=\"POST\">");
        responseContent.Append("<table border=\"0\">");
        responseContent.Append("<tr><td>Fill with value: <br> <input type=text name=\"info\" size=10></td></tr>");
        responseContent.Append("<tr><td>Fill with value: <br> <input type=text name=\"secondinfo\" size=20>");
        responseContent
                .Append("<tr><td>Fill with value: <br> <textarea name=\"thirdinfo\" cols=40 rows=10></textarea>");
        responseContent.Append("<tr><td>Fill with file: <br> <input type=file name=\"myfile\">");
        responseContent.Append("</td></tr>");
        responseContent.Append("<tr><td><INPUT TYPE=\"submit\" NAME=\"Send\" VALUE=\"Send\"></INPUT></td>");
        responseContent.Append("<td><INPUT TYPE=\"reset\" NAME=\"Clear\" VALUE=\"Clear\" ></INPUT></td></tr>");
        responseContent.Append("</table></FORM>\r\n");
        responseContent.Append("<CENTER><HR WIDTH=\"75%\" NOSHADE color=\"blue\"></CENTER>");

        responseContent.Append("</body>");
        responseContent.Append("</html>");

        ByteBuf buf = CopiedBuffer(responseContent.ToString(), Encoding.UTF8);
        // Build the response object.
        IFullHttpResponse response = new DefaultFullHttpResponse(
                HttpVersion.HTTP_1_1, HttpResponseStatus.OK, buf);

        response.Headers().Set(HttpHeaderNames.CONTENT_TYPE, "text/html; charset=UTF-8");
        response.Headers().SetInt(HttpHeaderNames.CONTENT_LENGTH, buf.ReadableBytes);

        // Decide whether to close the connection or not.
        bool keepAlive = HttpUtil.IsKeepAlive(request);
        if (!keepAlive)
        {
            response.Headers().Set(HttpHeaderNames.CONNECTION, HttpHeaderValues.CLOSE);
        }
        else if (request.ProtocolVersion().Equals(HttpVersion.HTTP_1_0))
        {
            response.Headers().Set(HttpHeaderNames.CONNECTION, HttpHeaderValues.KEEP_ALIVE);
        }

        // Write the response.
        IChannel channel = ctx.Channel;
        Task future = channel.WriteAndFlushAsync(response);
        // Close the connection after the write operation is done if necessary.
        if (!keepAlive)
        {
            _ = future.ContinueWith(_ => channel.CloseAsync(), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, new EventExecutorTaskScheduler(() => channel.EventLoop));
        }
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        logger.Warn(responseContent.ToString(), cause);
        _ = ctx.Channel.CloseAsync();
    }
}
