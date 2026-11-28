/*
 * Copyright 2023 The Netty Project
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

using System.Globalization;
using System.Net;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Quic;

/**
 * A network path specific {@link QuicEvent}.
 */
// CLR: InetSocketAddress is IPEndPoint, whose ToString() prints "address:port" where Java prints "host/address:port".
// Objects.requireNonNull(x, name) is ObjectUtil.CheckNotNull (ArgumentNullException for NullPointerException), and
// Objects.equals is object.Equals(a, b). The package-private constructor is internal; the nested final classes are
// sealed nested classes. getClass() is GetType(); seq >>> 32 is the C# unsigned right shift. toString() prints numbers
// with the invariant culture. System.Net.Quic reports no path events (connection migration is internal to msquic;
// docs/quic-design.md, No counterpart in System.Net.Quic): the events keep their Java API (owner decision 2026-11-27)
// and the msquic implementation (stage 2) never fires them.
public abstract class QuicPathEvent : IQuicEvent
{

    private readonly IPEndPoint local;
    private readonly IPEndPoint remote;

    internal QuicPathEvent(IPEndPoint local, IPEndPoint remote)
    {
        this.local = CheckNotNull(local, "local");
        this.remote = CheckNotNull(remote, "remote");
    }

    /**
     * The local address of the network path.
     *
     * @return  local
     */
    public IPEndPoint Local()
    {
        return local;
    }

    /**
     * The remote address of the network path.
     *
     * @return  local
     */
    public IPEndPoint Remote()
    {
        return remote;
    }

    public override string ToString()
    {
        return "QuicPathEvent{" +
                "local=" + local +
                ", remote=" + remote +
                '}';
    }

    public override bool Equals(object o)
    {
        if (ReferenceEquals(this, o))
        {
            return true;
        }
        if (o == null || GetType() != o.GetType())
        {
            return false;
        }

        QuicPathEvent that = (QuicPathEvent) o;
        if (!Equals(local, that.local))
        {
            return false;
        }

        return Equals(remote, that.remote);
    }

    public override int GetHashCode()
    {
        int result = local != null ? local.GetHashCode() : 0;
        result = unchecked(31 * result + (remote != null ? remote.GetHashCode() : 0));
        return result;
    }

    public sealed class New : QuicPathEvent
    {
        /**
         * A new network path (local address, remote address) has been seen on a received packet.
         * Note that this event is only triggered for servers, as the client is responsible from initiating new paths.
         * The application may then probe this new path, if desired.
         *
         * @param local     local address.
         * @param remote    remote address.
         */
        public New(IPEndPoint local, IPEndPoint remote) : base(local, remote)
        {
        }

        public override string ToString()
        {
            return "QuicPathEvent.New{" +
                    "local=" + Local() +
                    ", remote=" + Remote() +
                    '}';
        }
    }

    public sealed class Validated : QuicPathEvent
    {
        /**
         * The related network path between local and remote has been validated.
         *
         * @param local     local address.
         * @param remote    remote address.
         */
        public Validated(IPEndPoint local, IPEndPoint remote) : base(local, remote)
        {
        }

        public override string ToString()
        {
            return "QuicPathEvent.Validated{" +
                    "local=" + Local() +
                    ", remote=" + Remote() +
                    '}';
        }
    }

    public sealed class FailedValidation : QuicPathEvent
    {
        /**
         * The related network path between local and remote failed to be validated.
         * This network path will not be used anymore, unless the application requests probing this path again.
         *
         * @param local     local address.
         * @param remote    remote address.
         */
        public FailedValidation(IPEndPoint local, IPEndPoint remote) : base(local, remote)
        {
        }

        public override string ToString()
        {
            return "QuicPathEvent.FailedValidation{" +
                    "local=" + Local() +
                    ", remote=" + Remote() +
                    '}';
        }
    }

    public sealed class Closed : QuicPathEvent
    {

        /**
         * The related network path between local and remote has been closed and is now unusable on this connection.
         *
         * @param local     local address.
         * @param remote    remote address.
         */
        public Closed(IPEndPoint local, IPEndPoint remote) : base(local, remote)
        {
        }

        public override bool Equals(object o)
        {
            if (ReferenceEquals(this, o))
            {
                return true;
            }
            if (o == null || GetType() != o.GetType())
            {
                return false;
            }

            return base.Equals(o);
        }

        public override int GetHashCode()
        {
            return unchecked(31 * base.GetHashCode());
        }

        public override string ToString()
        {
            return "QuicPathEvent.Closed{" +
                    "local=" + Local() +
                    ", remote=" + Remote() +
                    '}';
        }
    }

    public sealed class ReusedSourceConnectionId : QuicPathEvent
    {
        private readonly long seq;
        private readonly IPEndPoint oldLocal;
        private readonly IPEndPoint oldRemote;

        /**
         * The stack observes that the Source Connection ID with the given sequence number,
         * initially used by the peer over the first pair of addresses, is now reused over
         * the second pair of addresses.
         *
         * @param seq           sequence number
         * @param oldLocal      old local address.
         * @param oldRemote     old remote address.
         * @param local         local address.
         * @param remote        remote address.
         */
        public ReusedSourceConnectionId(long seq, IPEndPoint oldLocal, IPEndPoint oldRemote,
                                        IPEndPoint local, IPEndPoint remote) : base(local, remote)
        {
            this.seq = seq;
            this.oldLocal = CheckNotNull(oldLocal, "oldLocal");
            this.oldRemote = CheckNotNull(oldRemote, "oldRemote");
        }

        /**
         * Source connection id sequence number.
         *
         * @return  sequence number
         */
        public long Seq()
        {
            return seq;
        }

        /**
         * The old local address of the network path.
         *
         * @return  local
         */
        public IPEndPoint OldLocal()
        {
            return oldLocal;
        }

        /**
         * The old remote address of the network path.
         *
         * @return  local
         */
        public IPEndPoint OldRemote()
        {
            return oldRemote;
        }

        public override bool Equals(object o)
        {
            if (ReferenceEquals(this, o))
            {
                return true;
            }
            if (o == null || GetType() != o.GetType())
            {
                return false;
            }
            if (!base.Equals(o))
            {
                return false;
            }

            ReusedSourceConnectionId that = (ReusedSourceConnectionId) o;

            if (seq != that.seq)
            {
                return false;
            }
            if (!Equals(oldLocal, that.oldLocal))
            {
                return false;
            }

            return Equals(oldRemote, that.oldRemote);
        }

        public override int GetHashCode()
        {
            int result = base.GetHashCode();
            result = unchecked(31 * result + (int) (seq ^ (seq >>> 32)));
            result = unchecked(31 * result + (oldLocal != null ? oldLocal.GetHashCode() : 0));
            result = unchecked(31 * result + (oldRemote != null ? oldRemote.GetHashCode() : 0));
            return result;
        }

        public override string ToString()
        {
            return "QuicPathEvent.ReusedSourceConnectionId{" +
                    "seq=" + seq.ToString(CultureInfo.InvariantCulture) +
                    ", oldLocal=" + oldLocal +
                    ", oldRemote=" + oldRemote +
                    ", local=" + Local() +
                    ", remote=" + Remote() +
                    '}';
        }
    }

    public sealed class PeerMigrated : QuicPathEvent
    {

        /**
         * The connection observed that the remote migrated over the network path denoted by the pair of addresses,
         * i.e., non-probing packets have been received on this network path. This is a server side only event.
         * Note that this event is only raised if the path has been validated.
         *
         * @param local     local address.
         * @param remote    remote address.
         */
        public PeerMigrated(IPEndPoint local, IPEndPoint remote) : base(local, remote)
        {
        }

        public override string ToString()
        {
            return "QuicPathEvent.PeerMigrated{" +
                    "local=" + Local() +
                    ", remote=" + Remote() +
                    '}';
        }
    }
}
