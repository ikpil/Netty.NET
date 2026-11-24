/*
 * Copyright 2014 The Netty Project
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
using System.Collections.ObjectModel;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Ssl;

/**
 * Provides an {@link SSLEngine} agnostic way to configure a {@link ApplicationProtocolNegotiator}.
 */
// CLR: the methods protocol(), selectorFailureBehavior() and selectedListenerFailureBehavior() would have the names of the
// nested enums Protocol, SelectorFailureBehavior and SelectedListenerFailureBehavior (CS0102), so they take a trailing
// underscore (owner decision 2026-11-23, as haproxy Type_()). Collections.unmodifiableList is a ReadOnlyCollection,
// "a collection with a wrapper that prevents modifying the collection"
// (https://learn.microsoft.com/en-us/dotnet/api/system.collections.objectmodel.readonlycollection-1?view=net-10.0);
// Collections.emptyList() is an empty one. The simple Java enums are C# enums (docs/codec-base-porting.md), value types,
// so the checkNotNull calls of the enum arguments have no counterpart. IllegalArgumentException is ArgumentException.
// The SSLEngine of the Javadoc is the SslStream that SslHandler drives (ssl design decision 2,
// docs/handler-ssl-design.md).
public sealed class ApplicationProtocolConfig
{

    /**
     * The configuration that disables application protocol negotiation.
     */
    public static readonly ApplicationProtocolConfig DISABLED = new ApplicationProtocolConfig();

    private readonly IList<string> supportedProtocols;
    private readonly Protocol protocol;
    private readonly SelectorFailureBehavior selectorBehavior;
    private readonly SelectedListenerFailureBehavior selectedBehavior;

    /**
     * Create a new instance.
     * @param protocol The application protocol functionality to use.
     * @param selectorBehavior How the peer selecting the protocol should behave.
     * @param selectedBehavior How the peer being notified of the selected protocol should behave.
     * @param supportedProtocols The order of iteration determines the preference of support for protocols.
     */
    public ApplicationProtocolConfig(Protocol protocol, SelectorFailureBehavior selectorBehavior,
            SelectedListenerFailureBehavior selectedBehavior, IEnumerable<string> supportedProtocols)
        : this(protocol, selectorBehavior, selectedBehavior, ApplicationProtocolUtil.ToList(supportedProtocols))
    {
    }

    /**
     * Create a new instance.
     * @param protocol The application protocol functionality to use.
     * @param selectorBehavior How the peer selecting the protocol should behave.
     * @param selectedBehavior How the peer being notified of the selected protocol should behave.
     * @param supportedProtocols The order of iteration determines the preference of support for protocols.
     */
    public ApplicationProtocolConfig(Protocol protocol, SelectorFailureBehavior selectorBehavior,
            SelectedListenerFailureBehavior selectedBehavior, params string[] supportedProtocols)
        : this(protocol, selectorBehavior, selectedBehavior, ApplicationProtocolUtil.ToList(supportedProtocols))
    {
    }

    /**
     * Create a new instance.
     * @param protocol The application protocol functionality to use.
     * @param selectorBehavior How the peer selecting the protocol should behave.
     * @param selectedBehavior How the peer being notified of the selected protocol should behave.
     * @param supportedProtocols The order of iteration determines the preference of support for protocols.
     */
    private ApplicationProtocolConfig(
            Protocol protocol, SelectorFailureBehavior selectorBehavior,
            SelectedListenerFailureBehavior selectedBehavior, IList<string> supportedProtocols)
    {
        this.supportedProtocols = new ReadOnlyCollection<string>(
                ObjectUtil.CheckNotNull(supportedProtocols, "supportedProtocols"));
        this.protocol = protocol;
        this.selectorBehavior = selectorBehavior;
        this.selectedBehavior = selectedBehavior;

        if (protocol == Protocol.NONE)
        {
            throw new ArgumentException("protocol (" + Protocol.NONE + ") must not be " + Protocol.NONE + '.');
        }
        ObjectUtil.CheckNonEmpty((ICollection<string>) supportedProtocols, "supportedProtocols");
    }

    /**
     * A special constructor that is used to instantiate {@link #DISABLED}.
     */
    private ApplicationProtocolConfig()
    {
        supportedProtocols = new ReadOnlyCollection<string>(Array.Empty<string>());
        protocol = Protocol.NONE;
        selectorBehavior = SelectorFailureBehavior.CHOOSE_MY_LAST_PROTOCOL;
        selectedBehavior = SelectedListenerFailureBehavior.ACCEPT;
    }

    /**
     * Defines which application level protocol negotiation to use.
     */
    public enum Protocol
    {
        NONE, NPN, ALPN, NPN_AND_ALPN
    }

    /**
     * Defines the most common behaviors for the peer that selects the application protocol.
     */
    public enum SelectorFailureBehavior
    {
        /**
         * If the peer who selects the application protocol doesn't find a match this will result in the failing the
         * handshake with a fatal alert.
         * <p>
         * For example in the case of ALPN this will result in a
         * <a herf="https://tools.ietf.org/html/rfc7301#section-3.2">no_application_protocol(120)</a> alert.
         */
        FATAL_ALERT,
        /**
         * If the peer who selects the application protocol doesn't find a match it will pretend no to support
         * the TLS extension by not advertising support for the TLS extension in the handshake. This is used in cases
         * where a "best effort" is desired to talk even if there is no matching protocol.
         */
        NO_ADVERTISE,
        /**
         * If the peer who selects the application protocol doesn't find a match it will just select the last protocol
         * it advertised support for. This is used in cases where a "best effort" is desired to talk even if there
         * is no matching protocol, and the assumption is the "most general" fallback protocol is typically listed last.
         * <p>
         * This may be <a href="https://tools.ietf.org/html/rfc7301#section-3.2">illegal for some RFCs</a> but was
         * observed behavior by some SSL implementations, and is supported for flexibility/compatibility.
         */
        CHOOSE_MY_LAST_PROTOCOL
    }

    /**
     * Defines the most common behaviors for the peer which is notified of the selected protocol.
     */
    public enum SelectedListenerFailureBehavior
    {
        /**
         * If the peer who is notified what protocol was selected determines the selection was not matched, or the peer
         * didn't advertise support for the TLS extension then the handshake will continue and the application protocol
         * is assumed to be accepted.
         */
        ACCEPT,
        /**
         * If the peer who is notified what protocol was selected determines the selection was not matched, or the peer
         * didn't advertise support for the TLS extension then the handshake will be failed with a fatal alert.
         */
        FATAL_ALERT,
        /**
         * If the peer who is notified what protocol was selected determines the selection was not matched, or the peer
         * didn't advertise support for the TLS extension then the handshake will continue assuming the last protocol
         * supported by this peer is used. This is used in cases where a "best effort" is desired to talk even if there
         * is no matching protocol, and the assumption is the "most general" fallback protocol is typically listed last.
         */
        CHOOSE_MY_LAST_PROTOCOL
    }

    /**
     * The application level protocols supported.
     */
    public IList<string> SupportedProtocols()
    {
        return supportedProtocols;
    }

    /**
     * Get which application level protocol negotiation to use.
     */
    public Protocol Protocol_()
    {
        return protocol;
    }

    /**
     * Get the desired behavior for the peer who selects the application protocol.
     */
    public SelectorFailureBehavior SelectorFailureBehavior_()
    {
        return selectorBehavior;
    }

    /**
     * Get the desired behavior for the peer who is notified of the selected protocol.
     */
    public SelectedListenerFailureBehavior SelectedListenerFailureBehavior_()
    {
        return selectedBehavior;
    }
}
