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

using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;
using static Netty.NET.Common.AsciiString;
using static Netty.NET.Common.Internal.ObjectUtil;
using static Netty.NET.Common.Internal.StringUtil;
using static Netty.NET.Handler.Codec.Http.HttpResponseStatus;
using static Netty.NET.Handler.Codec.Http.HttpVersion;

namespace Netty.NET.Handler.Codec.Http;

/**
 * A server-side handler that receives HTTP requests and optionally performs a protocol switch if
 * the requested protocol is supported. Once an upgrade is performed, this handler removes itself
 * from the pipeline.
 */
// CLR: UpgradeEvent implements IReferenceCounted; its narrowed retain/touch members return UpgradeEvent and the
// IReferenceCounted members are implemented explicitly. ChannelFutureListener.CLOSE_ON_FAILURE is a synchronous
// Task continuation that closes the channel when the write did not succeed
// (src/Netty.NET.Transport/Bootstrapping/AbstractBootstrap.cs records the same mapping);
// ctx.pipeline().remove(this) discards the returned pipeline. Character.isWhitespace(char) keeps the Java rule
// (see HttpVersion.IsJavaWhitespace), StringBuilder.setLength is the Length setter, and assert is Debug.Assert.
public class HttpServerUpgradeHandler : HttpObjectAggregator
{

    /**
     * The source codec that is used in the pipeline initially.
     */
    public interface ISourceCodec
    {
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
         * Gets all protocol-specific headers required by this protocol for a successful upgrade.
         * Any supplied header will be required to appear in the {@link HttpHeaderNames#CONNECTION} header as well.
         */
        ICollection<ICharSequence> RequiredUpgradeHeaders();

        /**
         * Prepares the {@code upgradeHeaders} for a protocol update based upon the contents of {@code upgradeRequest}.
         * This method returns a boolean value to proceed or abort the upgrade in progress. If {@code false} is
         * returned, the upgrade is aborted and the {@code upgradeRequest} will be passed through the inbound pipeline
         * as if no upgrade was performed. If {@code true} is returned, the upgrade will proceed to the next
         * step which invokes {@link #upgradeTo}. When returning {@code true}, you can add headers to
         * the {@code upgradeHeaders} so that they are added to the 101 Switching protocols response.
         */
        bool PrepareUpgradeResponse(IChannelHandlerContext ctx, IFullHttpRequest upgradeRequest,
                                    HttpHeaders upgradeHeaders);

        /**
         * Performs an HTTP protocol upgrade from the source codec. This method is responsible for
         * adding all handlers required for the new protocol.
         *
         * @param ctx the context for the current handler.
         * @param upgradeRequest the request that triggered the upgrade to this protocol.
         */
        void UpgradeTo(IChannelHandlerContext ctx, IFullHttpRequest upgradeRequest);
    }

    /**
     * Creates a new {@link UpgradeCodec} for the requested protocol name.
     */
    public interface IUpgradeCodecFactory
    {
        /**
         * Invoked by {@link HttpServerUpgradeHandler} for all the requested protocol names in the order of
         * the client preference. The first non-{@code null} {@link UpgradeCodec} returned by this method
         * will be selected.
         *
         * @return a new {@link UpgradeCodec}, or {@code null} if the specified protocol name is not supported
         */
        IUpgradeCodec NewUpgradeCodec(ICharSequence protocol);
    }

    /**
     * User event that is fired to notify about the completion of an HTTP upgrade
     * to another protocol. Contains the original upgrade request so that the response
     * (if required) can be sent using the new protocol.
     */
    public sealed class UpgradeEvent : IReferenceCounted
    {
        private readonly ICharSequence protocol;
        private readonly IFullHttpRequest upgradeRequest;

        internal UpgradeEvent(ICharSequence protocol, IFullHttpRequest upgradeRequest)
        {
            this.protocol = protocol;
            this.upgradeRequest = upgradeRequest;
        }

        /**
         * The protocol that the channel has been upgraded to.
         */
        public ICharSequence Protocol()
        {
            return protocol;
        }

        /**
         * Gets the request that triggered the protocol upgrade.
         */
        public IFullHttpRequest UpgradeRequest()
        {
            return upgradeRequest;
        }

        public int RefCnt => upgradeRequest.RefCnt;

        public UpgradeEvent Retain()
        {
            upgradeRequest.Retain();
            return this;
        }

        public UpgradeEvent Retain(int increment)
        {
            upgradeRequest.Retain(increment);
            return this;
        }

        public UpgradeEvent Touch()
        {
            upgradeRequest.Touch();
            return this;
        }

        public UpgradeEvent Touch(object hint)
        {
            upgradeRequest.Touch(hint);
            return this;
        }

        public bool Release()
        {
            return upgradeRequest.Release();
        }

        public bool Release(int decrement)
        {
            return upgradeRequest.Release(decrement);
        }

        public override string ToString()
        {
            return "UpgradeEvent [protocol=" + protocol + ", upgradeRequest=" + upgradeRequest + ']';
        }

        IReferenceCounted IReferenceCounted.Retain()
        {
            return Retain();
        }

        IReferenceCounted IReferenceCounted.Retain(int increment)
        {
            return Retain(increment);
        }

        IReferenceCounted IReferenceCounted.Touch()
        {
            return Touch();
        }

        IReferenceCounted IReferenceCounted.Touch(object hint)
        {
            return Touch(hint);
        }
    }

    private readonly ISourceCodec sourceCodec;
    private readonly IUpgradeCodecFactory upgradeCodecFactory;
    private readonly IHttpHeadersFactory headersFactory;
    private readonly IHttpHeadersFactory trailersFactory;
    private readonly bool removeAfterFirstRequest;
    private bool handlingUpgrade;
    private bool failedAggregationStart;

    /**
     * Constructs the upgrader with the supported codecs.
     * <p>
     * The handler instantiated by this constructor will reject an upgrade request with non-empty content.
     * It should not be a concern because an upgrade request is most likely a GET request.
     * If you have a client that sends a non-GET upgrade request, please consider using
     * {@link #HttpServerUpgradeHandler(SourceCodec, UpgradeCodecFactory, int)} to specify the maximum
     * length of the content of an upgrade request.
     * </p>
     *
     * @param sourceCodec the codec that is being used initially
     * @param upgradeCodecFactory the factory that creates a new upgrade codec
     *                            for one of the requested upgrade protocols
     */
    public HttpServerUpgradeHandler(ISourceCodec sourceCodec, IUpgradeCodecFactory upgradeCodecFactory)
        : this(sourceCodec, upgradeCodecFactory, 0,
                DefaultHttpHeadersFactory.HeadersFactory(), DefaultHttpHeadersFactory.TrailersFactory())
    {
    }

    /**
     * Constructs the upgrader with the supported codecs.
     *
     * @param sourceCodec the codec that is being used initially
     * @param upgradeCodecFactory the factory that creates a new upgrade codec
     *                            for one of the requested upgrade protocols
     * @param maxContentLength the maximum length of the content of an upgrade request
     */
    public HttpServerUpgradeHandler(
            ISourceCodec sourceCodec, IUpgradeCodecFactory upgradeCodecFactory, int maxContentLength)
        : this(sourceCodec, upgradeCodecFactory, maxContentLength,
                DefaultHttpHeadersFactory.HeadersFactory(), DefaultHttpHeadersFactory.TrailersFactory())
    {
    }

    /**
     * Constructs the upgrader with the supported codecs.
     *
     * @param sourceCodec the codec that is being used initially
     * @param upgradeCodecFactory the factory that creates a new upgrade codec
     *                            for one of the requested upgrade protocols
     * @param maxContentLength the maximum length of the content of an upgrade request
     * @param validateHeaders validate the header names and values of the upgrade response.
     */
    public HttpServerUpgradeHandler(ISourceCodec sourceCodec, IUpgradeCodecFactory upgradeCodecFactory,
                                    int maxContentLength, bool validateHeaders)
        : this(sourceCodec, upgradeCodecFactory, maxContentLength,
                DefaultHttpHeadersFactory.HeadersFactory().WithValidation(validateHeaders),
                DefaultHttpHeadersFactory.TrailersFactory().WithValidation(validateHeaders))
    {
    }

    /**
     * Constructs the upgrader with the supported codecs.
     *
     * @param sourceCodec the codec that is being used initially
     * @param upgradeCodecFactory the factory that creates a new upgrade codec
     *                            for one of the requested upgrade protocols
     * @param maxContentLength the maximum length of the content of an upgrade request
     * @param headersFactory The {@link HttpHeadersFactory} to use for headers.
     * The recommended default factory is {@link DefaultHttpHeadersFactory#headersFactory()}.
     * @param trailersFactory The {@link HttpHeadersFactory} to use for trailers.
     * The recommended default factory is {@link DefaultHttpHeadersFactory#trailersFactory()}.
     */
    public HttpServerUpgradeHandler(
            ISourceCodec sourceCodec, IUpgradeCodecFactory upgradeCodecFactory, int maxContentLength,
            IHttpHeadersFactory headersFactory, IHttpHeadersFactory trailersFactory)
        : this(sourceCodec, upgradeCodecFactory, maxContentLength, headersFactory, trailersFactory, false)
    {
    }

    /**
     * Constructs the upgrader with the supported codecs.
     *
     * @param sourceCodec the codec that is being used initially
     * @param upgradeCodecFactory the factory that creates a new upgrade codec
     *                            for one of the requested upgrade protocols
     * @param maxContentLength the maximum length of the content of an upgrade request
     * @param headersFactory The {@link HttpHeadersFactory} to use for headers.
     * The recommended default factory is {@link DefaultHttpHeadersFactory#headersFactory()}.
     * @param trailersFactory The {@link HttpHeadersFactory} to use for trailers.
     * The recommended default factory is {@link DefaultHttpHeadersFactory#trailersFactory()}.
     * @param removeAfterFirstRequest {@code true} if the handler should remove itself after the first request was
     *                                processed (even if it was not an upgrade request), {@code false} otherwise.
     */
    public HttpServerUpgradeHandler(
            ISourceCodec sourceCodec, IUpgradeCodecFactory upgradeCodecFactory, int maxContentLength,
            IHttpHeadersFactory headersFactory, IHttpHeadersFactory trailersFactory, bool removeAfterFirstRequest)
        : base(maxContentLength)
    {

        this.sourceCodec = CheckNotNull(sourceCodec, "sourceCodec");
        this.upgradeCodecFactory = CheckNotNull(upgradeCodecFactory, "upgradeCodecFactory");
        this.headersFactory = CheckNotNull(headersFactory, "headersFactory");
        this.trailersFactory = CheckNotNull(trailersFactory, "trailersFactory");
        this.removeAfterFirstRequest = removeAfterFirstRequest;
    }

    protected override void Decode(IChannelHandlerContext ctx, IHttpObject msg, IList<object> @out)
    {

        if (!handlingUpgrade)
        {
            // Not handling an upgrade request yet. Check if we received a new upgrade request.
            if (msg is IHttpRequest)
            {
                IHttpRequest req = (IHttpRequest) msg;
                if (req.Headers().Contains(HttpHeaderNames.UPGRADE) &&
                    ShouldHandleUpgradeRequest(req))
                {
                    handlingUpgrade = true;
                    failedAggregationStart = true; // reset if beginAggregation is called
                }
                else
                {
                    if (removeAfterFirstRequest)
                    {
                        // This is not an upgrade request, just remove this handler.
                        ctx.Pipeline.Remove(this);
                    }

                    ReferenceCountUtil.Retain(msg);
                    ctx.FireChannelRead(msg);
                    return;
                }
            }
            else
            {
                ReferenceCountUtil.Retain(msg);
                ctx.FireChannelRead(msg);
                return;
            }
        }

        IFullHttpRequest fullRequest;

        if (msg is IFullHttpRequest)
        {
            fullRequest = (IFullHttpRequest) msg;
            ReferenceCountUtil.Retain(msg);
            @out.Add(msg);
        }
        else
        {
            // Call the base class to handle the aggregation of the full request.
            base.Decode(ctx, msg, @out);

            if (@out.Count == 0)
            {
                if (msg is ILastHttpContent || failedAggregationStart)
                {
                    // request failed to aggregate, try with the next request
                    handlingUpgrade = false;
                    ReleaseCurrentMessage();
                }

                // The full request hasn't been created yet, still awaiting more data.
                return;
            }

            // Finished aggregating the full request, get it from the output list.
            Debug.Assert(@out.Count == 1);
            handlingUpgrade = false;
            fullRequest = (IFullHttpRequest) @out[0];
        }

        if (Upgrade(ctx, fullRequest))
        {
            // The upgrade was successful, remove the message from the output list
            // so that it's not propagated to the next handler. This request will
            // be propagated as a user event instead.
            @out.Clear();
        }
        else if (removeAfterFirstRequest)
        {
            // We handle the first request and were not able to upgrade, just remove this handler.
            ctx.Pipeline.Remove(this);
        }

        // The upgrade did not succeed, just allow the full request to propagate to the
        // next handler.
    }

    protected override IFullHttpMessage BeginAggregation(IHttpMessage start, ByteBuf content)
    {
        failedAggregationStart = false;
        return base.BeginAggregation(start, content);
    }

    /**
     * Determines whether the specified upgrade {@link HttpRequest} should be handled by this handler or not.
     * This method will be invoked only when the request contains an {@code Upgrade} header.
     * It always returns {@code true} by default, which means any request with an {@code Upgrade} header
     * will be handled. You can override this method to ignore certain {@code Upgrade} headers, for example:
     * <pre>{@code
     * @Override
     * protected boolean isUpgradeRequest(HttpRequest req) {
     *   // Do not handle WebSocket upgrades.
     *   return !req.headers().contains(HttpHeaderNames.UPGRADE, "websocket", false);
     * }
     * }</pre>
     */
    protected virtual bool ShouldHandleUpgradeRequest(IHttpRequest req)
    {
        return true;
    }

    /**
     * Attempts to upgrade to the protocol(s) identified by the {@link HttpHeaderNames#UPGRADE} header (if provided
     * in the request).
     *
     * @param ctx the context for this handler.
     * @param request the HTTP request.
     * @return {@code true} if the upgrade occurred, otherwise {@code false}.
     */
    private bool Upgrade(IChannelHandlerContext ctx, IFullHttpRequest request)
    {
        // Select the best protocol based on those requested in the UPGRADE header.
        IList<ICharSequence> requestedProtocols =
                SplitHeader(new StringCharSequence(request.Headers().Get(HttpHeaderNames.UPGRADE)));
        int numRequestedProtocols = requestedProtocols.Count;
        IUpgradeCodec upgradeCodec = null;
        ICharSequence upgradeProtocol = null;

        for (int i = 0; i < numRequestedProtocols; i ++)
        {
            ICharSequence p = requestedProtocols[i];
            IUpgradeCodec c = upgradeCodecFactory.NewUpgradeCodec(p);
            if (c != null)
            {
                upgradeProtocol = p;
                upgradeCodec = c;
                break;
            }
        }

        if (upgradeCodec == null)
        {
            // None of the requested protocols are supported, don't upgrade.
            return false;
        }

        // Make sure the CONNECTION header is present.
        IList<string> connectionHeaderValues = request.Headers().GetAll(HttpHeaderNames.CONNECTION);

        if (connectionHeaderValues == null || connectionHeaderValues.Count == 0)
        {
            return false;
        }

        StringBuilder concatenatedConnectionValue = new StringBuilder(connectionHeaderValues.Count * 10);

        foreach (string connectionHeaderValue in connectionHeaderValues)
        {
            concatenatedConnectionValue.Append(connectionHeaderValue).Append(COMMA);
        }

        concatenatedConnectionValue.Length = concatenatedConnectionValue.Length - 1;

        // Make sure the CONNECTION header contains UPGRADE as well as all protocol-specific headers.
        ICollection<ICharSequence> requiredHeaders = upgradeCodec.RequiredUpgradeHeaders();
        IList<ICharSequence> values = SplitHeader(new StringCharSequence(concatenatedConnectionValue.ToString()));
        if (!ContainsContentEqualsIgnoreCase(values, HttpHeaderNames.UPGRADE) ||
                !ContainsAllContentEqualsIgnoreCase(values, requiredHeaders))
        {
            return false;
        }

        // Ensure that all required protocol-specific headers are found in the request.
        foreach (ICharSequence requiredHeader in requiredHeaders)
        {
            if (!request.Headers().Contains(requiredHeader))
            {
                return false;
            }
        }

        // Prepare and send the upgrade response. Wait for this write to complete before upgrading,
        // since we need the old codec in-place to properly encode the response.
        IFullHttpResponse upgradeResponse = CreateUpgradeResponse(upgradeProtocol);
        if (!upgradeCodec.PrepareUpgradeResponse(ctx, request, upgradeResponse.Headers()))
        {
            return false;
        }

        // Create the user event to be fired once the upgrade completes.
        UpgradeEvent @event = new UpgradeEvent(upgradeProtocol, request);

        // After writing the upgrade response we immediately prepare the
        // pipeline for the next protocol to avoid a race between completion
        // of the write future and receiving data before the pipeline is
        // restructured.
        try
        {
            Task writeComplete = ctx.WriteAndFlushAsync(upgradeResponse);
            // Perform the upgrade to the new protocol.
            sourceCodec.UpgradeFrom(ctx);
            upgradeCodec.UpgradeTo(ctx, request);

            // Remove this handler from the pipeline.
            ctx.Pipeline.Remove(this);

            // Notify that the upgrade has occurred. Retain the event to offset
            // the release() in the finally block.
            ctx.FireUserEventTriggered(@event.Retain());

            // Add the listener last to avoid firing upgrade logic after
            // the channel is already closed since the listener may fire
            // immediately if the write failed eagerly.
            IChannel channel = ctx.Channel;
            _ = writeComplete.ContinueWith(f =>
            {
                if (!f.IsCompletedSuccessfully)
                {
                    _ = channel.CloseAsync();
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        finally
        {
            // Release the event if the upgrade event wasn't fired.
            @event.Release();
        }

        return true;
    }

    /**
     * Creates the 101 Switching Protocols response message.
     */
    private IFullHttpResponse CreateUpgradeResponse(ICharSequence upgradeProtocol)
    {
        DefaultFullHttpResponse res = new DefaultFullHttpResponse(
                HTTP_1_1, SWITCHING_PROTOCOLS, Unpooled.EmptyBuffer, headersFactory, trailersFactory);
        res.Headers().Add(HttpHeaderNames.CONNECTION, HttpHeaderValues.UPGRADE);
        res.Headers().Add(HttpHeaderNames.UPGRADE, upgradeProtocol);
        return res;
    }

    /**
     * Splits a comma-separated header value. The returned set is case-insensitive and contains each
     * part with whitespace removed.
     */
    private static IList<ICharSequence> SplitHeader(ICharSequence header)
    {
        StringBuilder builder = new StringBuilder(header.Length());
        IList<ICharSequence> protocols = new List<ICharSequence>(4);

        for (int i = 0; i < header.Length(); ++i)
        {
            char c = header.CharAt(i);
            if (IsJavaWhitespace(c))
            {
                // Don't include any whitespace.
                continue;
            }

            if (c == ',')
            {
                // Add the string and reset the builder for the next protocol.
                protocols.Add(new StringCharSequence(builder.ToString()));
                builder.Length = 0;
            }
            else
            {
                builder.Append(c);
            }
        }

        // Add the last protocol
        if (builder.Length > 0)
        {
            protocols.Add(new StringCharSequence(builder.ToString()));
        }

        return protocols;
    }

    // CLR: Character.isWhitespace(char) (see HttpVersion).
    private static bool IsJavaWhitespace(char c)
    {
        return HttpVersion.IsJavaWhitespace(c);
    }
}
