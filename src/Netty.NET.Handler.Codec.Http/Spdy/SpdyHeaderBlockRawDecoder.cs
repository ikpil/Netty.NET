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
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using static Netty.NET.Handler.Codec.Spdy.SpdyCodecUtil;

namespace Netty.NET.Handler.Codec.Spdy;

// CLR: the package-private overrides are internal and the protected decodeHeaderBlock is protected virtual (Java
// methods are virtual unless final). The simple enum State is a private C# enum; the unreachable default branch
// (java.lang.Error) throws InvalidOperationException, as codec-base AsciiHeadersEncoder does. new String(bytes, UTF_8)
// is Encoding.UTF8.GetString, which also replaces malformed input with U+FFFD. The header name and values, Strings that
// Java adds to the CharSequence headers, are wrapped in the Common StringCharSequence (docs/codec-base-porting.md,
// Headers; the core codec-http mapping of a String passed as CharSequence). catch (IllegalArgumentException) is catch
// (ArgumentException) (docs/codec-base-porting.md). readBytes(byte[]) is ReadBytes(Span<byte>). The Java byte
// comparisons with (byte) 0 compare bit patterns.
public class SpdyHeaderBlockRawDecoder : SpdyHeaderBlockDecoder
{

    private const int LENGTH_FIELD_SIZE = 4;

    private readonly int maxHeaderSize;

    private State state;

    private ByteBuf cumulation;

    private int headerSize;
    private int numHeaders;
    private int length;
    private string name;

    private enum State
    {
        READ_NUM_HEADERS,
        READ_NAME_LENGTH,
        READ_NAME,
        SKIP_NAME,
        READ_VALUE_LENGTH,
        READ_VALUE,
        SKIP_VALUE,
        END_HEADER_BLOCK,
        ERROR
    }

    public SpdyHeaderBlockRawDecoder(SpdyVersion spdyVersion, int maxHeaderSize)
    {
        ObjectUtil.CheckNotNull(spdyVersion, "spdyVersion");

        this.maxHeaderSize = maxHeaderSize;
        state = State.READ_NUM_HEADERS;
    }

    private static int ReadLengthField(ByteBuf buffer)
    {
        int length = GetSignedInt(buffer, buffer.ReaderIndex);
        buffer.SkipBytes(LENGTH_FIELD_SIZE);
        return length;
    }

    internal override void Decode(IByteBufAllocator alloc, ByteBuf headerBlock, ISpdyHeadersFrame frame)
    {
        ObjectUtil.CheckNotNull(headerBlock, "headerBlock");
        ObjectUtil.CheckNotNull(frame, "frame");

        if (cumulation == null)
        {
            DecodeHeaderBlock(headerBlock, frame);

            if (headerBlock.IsReadable())
            {
                cumulation = alloc.Buffer(headerBlock.ReadableBytes);
                cumulation.WriteBytes(headerBlock);
            }
        }
        else
        {
            cumulation.WriteBytes(headerBlock);
            DecodeHeaderBlock(cumulation, frame);

            if (cumulation.IsReadable())
            {
                cumulation.DiscardReadBytes();
            }
            else
            {
                ReleaseBuffer();
            }
        }
    }

    protected virtual void DecodeHeaderBlock(ByteBuf headerBlock, ISpdyHeadersFrame frame)
    {
        int skipLength;

        while (headerBlock.IsReadable())
        {
            switch(state)
            {
                case State.READ_NUM_HEADERS:
                    if (headerBlock.ReadableBytes < LENGTH_FIELD_SIZE)
                    {
                        return;
                    }

                    numHeaders = ReadLengthField(headerBlock);

                    if (numHeaders < 0)
                    {
                        state = State.ERROR;
                        frame.SetInvalid();
                    }
                    else if (numHeaders == 0)
                    {
                        state = State.END_HEADER_BLOCK;
                    }
                    else
                    {
                        state = State.READ_NAME_LENGTH;
                    }

                    break;

                case State.READ_NAME_LENGTH:
                    if (headerBlock.ReadableBytes < LENGTH_FIELD_SIZE)
                    {
                        return;
                    }

                    length = ReadLengthField(headerBlock);

                    // Recipients of a zero-length name must issue a stream error
                    if (length <= 0)
                    {
                        state = State.ERROR;
                        frame.SetInvalid();
                    }
                    else if (length > maxHeaderSize || headerSize > maxHeaderSize - length)
                    {
                        headerSize = maxHeaderSize + 1;
                        state = State.SKIP_NAME;
                        frame.SetTruncated();
                    }
                    else
                    {
                        headerSize += length;
                        state = State.READ_NAME;
                    }

                    break;

                case State.READ_NAME:
                    if (headerBlock.ReadableBytes < length)
                    {
                        return;
                    }

                    byte[] nameBytes = new byte[length];
                    headerBlock.ReadBytes(nameBytes);
                    name = Encoding.UTF8.GetString(nameBytes);

                    // Check for identically named headers
                    if (frame.Headers().Contains(new StringCharSequence(name)))
                    {
                        state = State.ERROR;
                        frame.SetInvalid();
                    }
                    else
                    {
                        state = State.READ_VALUE_LENGTH;
                    }

                    break;

                case State.SKIP_NAME:
                    skipLength = Math.Min(headerBlock.ReadableBytes, length);
                    headerBlock.SkipBytes(skipLength);
                    length -= skipLength;

                    if (length == 0)
                    {
                        state = State.READ_VALUE_LENGTH;
                    }

                    break;

                case State.READ_VALUE_LENGTH:
                    if (headerBlock.ReadableBytes < LENGTH_FIELD_SIZE)
                    {
                        return;
                    }

                    length = ReadLengthField(headerBlock);

                    // Recipients of illegal value fields must issue a stream error
                    if (length < 0)
                    {
                        state = State.ERROR;
                        frame.SetInvalid();
                    }
                    else if (length == 0)
                    {
                        if (!frame.IsTruncated())
                        {
                            // SPDY/3 allows zero-length (empty) header values
                            frame.Headers().Add(new StringCharSequence(name), StringCharSequence.Empty);
                        }

                        name = null;

                        if (--numHeaders == 0)
                        {
                            state = State.END_HEADER_BLOCK;
                        }
                        else
                        {
                            state = State.READ_NAME_LENGTH;
                        }

                    }
                    else if (length > maxHeaderSize || headerSize > maxHeaderSize - length)
                    {
                        headerSize = maxHeaderSize + 1;
                        name = null;
                        state = State.SKIP_VALUE;
                        frame.SetTruncated();
                    }
                    else
                    {
                        headerSize += length;
                        state = State.READ_VALUE;
                    }

                    break;

                case State.READ_VALUE:
                    if (headerBlock.ReadableBytes < length)
                    {
                        return;
                    }

                    byte[] valueBytes = new byte[length];
                    headerBlock.ReadBytes(valueBytes);

                    // Add Name/Value pair to headers
                    int index = 0;
                    int offset = 0;

                    // Value must not start with a NULL character
                    if (valueBytes[0] == (byte) 0)
                    {
                        state = State.ERROR;
                        frame.SetInvalid();
                        break;
                    }

                    while (index < length)
                    {
                        while (index < valueBytes.Length && valueBytes[index] != (byte) 0)
                        {
                            index ++;
                        }

                        if (index < valueBytes.Length)
                        {
                            // Received NULL character
                            if (index + 1 == valueBytes.Length || valueBytes[index + 1] == (byte) 0)
                            {
                                // Value field ended with a NULL character or
                                // received multiple, in-sequence NULL characters.
                                // Recipients of illegal value fields must issue a stream error
                                state = State.ERROR;
                                frame.SetInvalid();
                                break;
                            }
                        }

                        string value = Encoding.UTF8.GetString(valueBytes, offset, index - offset);

                        try
                        {
                            frame.Headers().Add(new StringCharSequence(name), new StringCharSequence(value));
                        }
                        catch (ArgumentException)
                        {
                            // Name contains NULL or non-ascii characters
                            state = State.ERROR;
                            frame.SetInvalid();
                            break;
                        }

                        index ++;
                        offset = index;
                    }

                    name = null;

                    // If we broke out of the add header loop, break here
                    if (state == State.ERROR)
                    {
                        break;
                    }

                    if (--numHeaders == 0)
                    {
                        state = State.END_HEADER_BLOCK;
                    }
                    else
                    {
                        state = State.READ_NAME_LENGTH;
                    }

                    break;

                case State.SKIP_VALUE:
                    skipLength = Math.Min(headerBlock.ReadableBytes, length);
                    headerBlock.SkipBytes(skipLength);
                    length -= skipLength;

                    if (length == 0)
                    {
                        if (--numHeaders == 0)
                        {
                            state = State.END_HEADER_BLOCK;
                        }
                        else
                        {
                            state = State.READ_NAME_LENGTH;
                        }
                    }

                    break;

                case State.END_HEADER_BLOCK:
                    state = State.ERROR;
                    frame.SetInvalid();
                    break;

                case State.ERROR:
                    headerBlock.SkipBytes(headerBlock.ReadableBytes);
                    return;

                default:
                    throw new InvalidOperationException("Unexpected state: " + state);
            }
        }
    }

    internal override void EndHeaderBlock(ISpdyHeadersFrame frame)
    {
        if (state != State.END_HEADER_BLOCK)
        {
            frame.SetInvalid();
        }

        ReleaseBuffer();

        // Initialize header block decoding fields
        headerSize = 0;
        name = null;
        state = State.READ_NUM_HEADERS;
    }

    internal override void End()
    {
        ReleaseBuffer();
    }

    private void ReleaseBuffer()
    {
        if (cumulation != null)
        {
            cumulation.Release();
            cumulation = null;
        }
    }
}
