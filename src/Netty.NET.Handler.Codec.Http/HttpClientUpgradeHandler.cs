/*
 * Copyright 2014 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Collections;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Http.HttpResponseStatus;

namespace Netty.NET.Handler.Codec.Http;

/**
 * Client-side handler for handling an HTTP upgrade handshake to another protocol. When the first
 * HTTP request is sent, this handler will add all appropriate headers to perform an upgrade to the
 * new protocol. If the upgrade fails (i.e. response is not 101 Switching Protocols), this handler
 * simply removes itself from the pipeline. If the upgrade is successful, upgrades the pipeline to
 * the new protocol.
 */
// CLR: the nested interfaces take the I prefix (ISourceCodec, IUpgradeCodec); the simple enum UpgradeEvent is a C# enum
// and currentUpgradeEvent, which Java resets to null, is UpgradeEvent?. The ChannelOutboundHandler operations are the
// Task-returning *Async members of IChannelOutboundHandler, whose Task replaces the promise
// (docs/codec-base-porting.md, Outbound operations); promise.setFailure(...) is a faulted Task. SocketAddress is EndPoint
// (docs/handler-porting.md, Addresses), Collection<CharSequence> is ICollection<ICharSequence>, LinkedHashSet is the
// Common LinkedHashSet, IllegalStateException is InvalidOperationException, catch (Throwable) is catch (Exception) and
// assert is Debug.Assert. The UPGRADE header value is a String in Java and a CharSequence there; it is wrapped in
// StringCharSequence for AsciiString.contentEqualsIgnoreCase.
public class HttpClientUpgradeHandler : HttpObjectAggregator, IChannelOutboundHandler
{

    /**
     * User events that are fired to notify about upgrade status.
     */
    public enum UpgradeEvent
    {
        /**
         * The Upgrade request was sent to the server.
         */
        UPGRADE_ISSUED,

        /**
         * The Upgrade to the new protocol was successful.
         */
        UPGRADE_SUCCESSFUL,

        /**
         * The Upgrade was unsuccessful due to the server not issuing
         * with a 101 Switching Protocols response.
         */
        UPGRADE_REJECTED
    }

    /**
     * The source codec that is used in the pipeline initially.
     */
    public interface ISourceCodec
    {

        /**
         * Removes or disables the encoder of this codec so that the {@link UpgradeCodec} can send an initial greeting
         * (if any).
         */
        void PrepareUpgradeFrom(IChannelHandlerContext ctx);

        /**
         * Removes this codec (i.e. all associated handlers) from the pipeline.
         */
        void UpgradeFrom(IChannelHandlerContext ctx);
    }

    /**
     * A codec that the source can be upgraded to.
     */
    public interface IUpgradeCodec
    {
        /**
         * Returns the name of the protocol supported by this codec, as indicated by the {@code 'UPGRADE'} header.
         */
        ICharSequence Protocol();

        /**
         * Sets any protocol-specific headers required to the upgrade request. Returns the names of
         * all headers that were added. These headers will be used to populate the CONNECTION header.
         */
        ICollection<ICharSequence> SetUpgradeHeaders(IChannelHandlerContext ctx, IHttpRequest upgradeRequest);

        /**
         * Performs an HTTP protocol upgrade from the source codec. This method is responsible for
         * adding all handlers required for the new protocol.
         *
         * @param ctx the context for the current handler.
         * @param upgradeResponse the 101 Switching Protocols response that indicates that the server
         *            has switched to this protocol.
         */
        void UpgradeTo(IChannelHandlerContext ctx, IFullHttpResponse upgradeResponse);
    }

    private readonly ISourceCodec sourceCodec;
    private readonly IUpgradeCodec upgradeCodec;
    private UpgradeEvent? currentUpgradeEvent;

    /**
     * Constructs the client upgrade handler.
     *
     * @param sourceCodec the codec that is being used initially.
     * @param upgradeCodec the codec that the client would like to upgrade to.
     * @param maxContentLength the maximum length of the aggregated content.
     */
    public HttpClientUpgradeHandler(ISourceCodec sourceCodec, IUpgradeCodec upgradeCodec,
                                    int maxContentLength)
        : base(maxContentLength)
    {
        this.sourceCodec = ObjectUtil.CheckNotNull(sourceCodec, "sourceCodec");
        this.upgradeCodec = ObjectUtil.CheckNotNull(upgradeCodec, "upgradeCodec");
    }

    public virtual Task BindAsync(IChannelHandlerContext ctx, EndPoint localAddress, CancellationToken cancellationToken)
    {
        return ctx.BindAsync(localAddress, cancellationToken);
    }

    public virtual Task ConnectAsync(IChannelHandlerContext ctx, EndPoint remoteAddress, EndPoint localAddress,
                        CancellationToken cancellationToken)
    {
        return ctx.ConnectAsync(remoteAddress, localAddress, cancellationToken);
    }

    public virtual Task DisconnectAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
    {
        return ctx.DisconnectAsync(cancellationToken);
    }

    public virtual Task CloseAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
    {
        return ctx.CloseAsync(cancellationToken);
    }

    public virtual Task DeregisterAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
    {
        return ctx.DeregisterAsync(cancellationToken);
    }

    public virtual void Read(IChannelHandlerContext ctx)
    {
        ctx.Read();
    }

    public virtual Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
    {
        if (!(msg is IHttpRequest) || currentUpgradeEvent == UpgradeEvent.UPGRADE_SUCCESSFUL)
        {
            return ctx.WriteAsync(msg, cancellationToken);
        }

        if (currentUpgradeEvent == UpgradeEvent.UPGRADE_ISSUED)
        {
            // Release message before failing the promise.
            ReferenceCountUtil.Release(msg);

            return Task.FromException(new InvalidOperationException(
                    "Attempting to write HTTP request with upgrade in progress"));
        }

        currentUpgradeEvent = UpgradeEvent.UPGRADE_ISSUED;
        SetUpgradeRequestHeaders(ctx, (IHttpRequest) msg);

        // Continue writing the request.
        Task future = ctx.WriteAsync(msg, cancellationToken);

        // Notify that the upgrade request was issued.
        ctx.FireUserEventTriggered(UpgradeEvent.UPGRADE_ISSUED);
        // Now we wait for the next HTTP response to see if we switch protocols.
        return future;
    }

    public virtual void Flush(IChannelHandlerContext ctx)
    {
        ctx.Flush();
    }

    protected override void Decode(IChannelHandlerContext ctx, IHttpObject msg, IList<object> @out)
    {
        IFullHttpResponse response = null;

        try
        {
            if (currentUpgradeEvent != UpgradeEvent.UPGRADE_ISSUED)
            {
                throw new InvalidOperationException("Read HTTP response without requesting protocol switch");
            }

            if (msg is IHttpResponse)
            {
                IHttpResponse rep = (IHttpResponse) msg;
                if (!SWITCHING_PROTOCOLS.Equals(rep.Status()))
                {
                    // The server does not support the requested protocol, just remove this handler
                    // and continue processing HTTP.
                    // NOTE: not releasing the response since we're letting it propagate to the
                    // next handler.
                    currentUpgradeEvent = null;
                    ctx.FireUserEventTriggered(UpgradeEvent.UPGRADE_REJECTED);
                    RemoveThisHandler(ctx);
                    ctx.FireChannelRead(msg);
                    return;
                }
            }

            if (msg is IFullHttpResponse)
            {
                response = (IFullHttpResponse) msg;
                // Need to retain since the base class will release after returning from this method.
                response.Retain();
                @out.Add(response);
            }
            else
            {
                // Call the base class to handle the aggregation of the full request.
                base.Decode(ctx, msg, @out);
                if (@out.Count == 0)
                {
                    // The full request hasn't been created yet, still awaiting more data.
                    return;
                }

                Debug.Assert(@out.Count == 1);
                response = (IFullHttpResponse) @out[0];
            }

            string upgradeHeader = response.Headers().Get(HttpHeaderNames.UPGRADE);
            if (upgradeHeader != null &&
                    !AsciiString.ContentEqualsIgnoreCase(upgradeCodec.Protocol(), new StringCharSequence(upgradeHeader)))
            {
                throw new InvalidOperationException(
                        "Switching Protocols response with unexpected UPGRADE protocol: " + upgradeHeader);
            }

            // Upgrade to the new protocol.
            sourceCodec.PrepareUpgradeFrom(ctx);
            upgradeCodec.UpgradeTo(ctx, response);

            // Let's set currentUpgradeEvent to UPGRADE_SUCCESSFUL as we will notify the pipeline about the successful
            // upgrade now, which might results in a write that needs to be passed through.
            currentUpgradeEvent = UpgradeEvent.UPGRADE_SUCCESSFUL;

            // Notify that the upgrade to the new protocol completed successfully.
            ctx.FireUserEventTriggered(UpgradeEvent.UPGRADE_SUCCESSFUL);

            // We guarantee UPGRADE_SUCCESSFUL event will be arrived at the next handler
            // before http2 setting frame and http response.
            sourceCodec.UpgradeFrom(ctx);

            // We switched protocols, so we're done with the upgrade response.
            // Release it and clear it from the output.
            response.Release();
            @out.Clear();
            RemoveThisHandler(ctx);
        }
        catch (Exception t)
        {
            ctx.FireExceptionCaught(t);
            RemoveThisHandler(ctx);
        }
    }

    private static void RemoveThisHandler(IChannelHandlerContext ctx)
    {
        ctx.Pipeline.Remove(ctx.Name);
    }

    /**
     * Adds all upgrade request headers necessary for an upgrade to the supported protocols.
     */
    private void SetUpgradeRequestHeaders(IChannelHandlerContext ctx, IHttpRequest request)
    {
        // Set the UPGRADE header on the request.
        request.Headers().Set(HttpHeaderNames.UPGRADE, upgradeCodec.Protocol());

        // Add all protocol-specific headers to the request.
        ISet<ICharSequence> connectionParts = new LinkedHashSet<ICharSequence>(2);
        connectionParts.UnionWith(upgradeCodec.SetUpgradeHeaders(ctx, request));

        // Set the CONNECTION header from the set of all protocol-specific headers that were added.
        StringBuilder builder = new StringBuilder();

        foreach (ICharSequence part in connectionParts)
        {
            builder.Append(part);
            builder.Append(',');
        }

        builder.Append(HttpHeaderValues.UPGRADE);
        request.Headers().Add(HttpHeaderNames.CONNECTION, builder.ToString());
    }
}
