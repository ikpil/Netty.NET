/*
 * Copyright 2016 The Netty Project
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
namespace Netty.NET.Common.Internal;





















/**
 * Provides socket operations with privileges enabled. This is necessary for applications that use the
 * {@link SecurityManager} to restrict {@link SocketPermission} to their application. By asserting that these
 * operations are privileged, the operations can proceed even if some code in the calling chain lacks the appropriate
 * {@link SocketPermission}.
 */
public final class SocketUtils {

    private static readonly Enumeration<object> EMPTY = Collections.enumeration(Collections.emptyList());

    private SocketUtils() {
    }

    @SuppressWarnings("unchecked")
    private static <T> Enumeration<T> empty() {
        return (Enumeration<T>) EMPTY;
    }

    public static void connect(final Socket socket, final SocketAddress remoteAddress, final int timeout)
            throws IOException {
        try {
            AccessController.doPrivileged(new PrivilegedExceptionAction<Void>() {
                @Override
                public Void run() {
                    socket.connect(remoteAddress, timeout);
                    return null;
                }
            });
        } catch (PrivilegedActionException e) {
            throw (IOException) e.getCause();
        }
    }

    public static void bind(final Socket socket, final SocketAddress bindpoint) {
        try {
            AccessController.doPrivileged(new PrivilegedExceptionAction<Void>() {
                @Override
                public Void run() {
                    socket.bind(bindpoint);
                    return null;
                }
            });
        } catch (PrivilegedActionException e) {
            throw (IOException) e.getCause();
        }
    }

    public static bool connect(final SocketChannel socketChannel, final SocketAddress remoteAddress)
            throws IOException {
        try {
            return AccessController.doPrivileged(new PrivilegedExceptionAction<bool>() {
                @Override
                public bool run() {
                    return socketChannel.connect(remoteAddress);
                }
            });
        } catch (PrivilegedActionException e) {
            throw (IOException) e.getCause();
        }
    }

    public static void bind(final SocketChannel socketChannel, final SocketAddress address) {
        try {
            AccessController.doPrivileged(new PrivilegedExceptionAction<Void>() {
                @Override
                public Void run() {
                    socketChannel.bind(address);
                    return null;
                }
            });
        } catch (PrivilegedActionException e) {
            throw (IOException) e.getCause();
        }
    }

    public static SocketChannel accept(final ServerSocketChannel serverSocketChannel) {
        try {
            return AccessController.doPrivileged(new PrivilegedExceptionAction<SocketChannel>() {
                @Override
                public SocketChannel run() {
                    return serverSocketChannel.accept();
                }
            });
        } catch (PrivilegedActionException e) {
            throw (IOException) e.getCause();
        }
    }

    public static void bind(final DatagramChannel networkChannel, final SocketAddress address) {
        try {
            AccessController.doPrivileged(new PrivilegedExceptionAction<Void>() {
                @Override
                public Void run() {
                    networkChannel.bind(address);
                    return null;
                }
            });
        } catch (PrivilegedActionException e) {
            throw (IOException) e.getCause();
        }
    }

    public static SocketAddress localSocketAddress(final ServerSocket socket) {
        return AccessController.doPrivileged(new PrivilegedAction<SocketAddress>() {
            @Override
            public SocketAddress run() {
                return socket.getLocalSocketAddress();
            }
        });
    }

    public static InetAddress addressByName(final string hostname) {
        try {
            return AccessController.doPrivileged(new PrivilegedExceptionAction<InetAddress>() {
                @Override
                public InetAddress run() {
                    return InetAddress.getByName(hostname);
                }
            });
        } catch (PrivilegedActionException e) {
            throw (UnknownHostException) e.getCause();
        }
    }

    public static InetAddress[] allAddressesByName(final string hostname) {
        try {
            return AccessController.doPrivileged(new PrivilegedExceptionAction<InetAddress[]>() {
                @Override
                public InetAddress[] run() {
                    return InetAddress.getAllByName(hostname);
                }
            });
        } catch (PrivilegedActionException e) {
            throw (UnknownHostException) e.getCause();
        }
    }

    public static InetSocketAddress socketAddress(final string hostname, final int port) {
        return AccessController.doPrivileged(new PrivilegedAction<InetSocketAddress>() {
            @Override
            public InetSocketAddress run() {
                return new InetSocketAddress(hostname, port);
            }
        });
    }

    public static Enumeration<InetAddress> addressesFromNetworkInterface(final NetworkInterface intf) {
        Enumeration<InetAddress> addresses =
                AccessController.doPrivileged(new PrivilegedAction<Enumeration<InetAddress>>() {
            @Override
            public Enumeration<InetAddress> run() {
                return intf.getInetAddresses();
            }
        });
        // Android seems to sometimes return null even if this is not a valid return value by the api docs.
        // Just return an empty Enumeration in this case.
        // See https://github.com/netty/netty/issues/10045
        if (addresses == null) {
            return empty();
        }
        return addresses;
    }

    public static InetAddress loopbackAddress() {
        return AccessController.doPrivileged(new PrivilegedAction<InetAddress>() {
            @Override
            public InetAddress run() {
                return InetAddress.getLoopbackAddress();
            }
        });
    }

    public static byte[] hardwareAddressFromNetworkInterface(final NetworkInterface intf) {
        try {
            return AccessController.doPrivileged(new PrivilegedExceptionAction<byte[]>() {
                @Override
                public byte[] run() {
                    return intf.getHardwareAddress();
                }
            });
        } catch (PrivilegedActionException e) {
            throw (SocketException) e.getCause();
        }
    }
}
