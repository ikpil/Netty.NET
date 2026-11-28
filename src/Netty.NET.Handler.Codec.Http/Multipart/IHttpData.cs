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

using System.IO;
using System.Text;
using Netty.NET.Buffer;

namespace Netty.NET.Handler.Codec.Http.Multipart;

/**
 * Extended interface for InterfaceHttpData
 */
// CLR: the Java interface HttpData takes the I prefix (owner rule); ByteBufHolder is IByteBufHolder, whose
// content() is the read-only Content property. java.io.File is FileInfo, InputStream is System.IO.Stream (written
// in full, because inside Netty.NET.Handler.* the simple name Stream binds to the namespace
// Netty.NET.Handler.Stream) and Charset is Encoding; IOException is System.IO.IOException, which "throws
// IOException" only documents in C#. get() is Get(). copy(), duplicate(), retainedDuplicate(), replace(ByteBuf),
// retain(), retain(int), touch() and touch(Object) are re-declared only to narrow the return type, so they are
// inherited from IByteBufHolder, not repeated; the classes narrow them with covariant override returns.
public interface IHttpData : IInterfaceHttpData, IByteBufHolder
{

    /**
     * Returns the maxSize for this HttpData.
     */
    long GetMaxSize();

    /**
     * Set the maxSize for this HttpData. When limit will be reached, an exception will be raised.
     * Setting it to (-1) means no limitation.
     *
     * By default, to be set from the HttpDataFactory.
     */
    void SetMaxSize(long maxSize);

    /**
     * Check if the new size is not reaching the max limit allowed.
     * The limit is always computed in terms of bytes.
     */
    void CheckSize(long newSize);

    /**
     * Set the content from the ChannelBuffer (erase any previous data)
     * <p>{@link ByteBuf#release()} ownership of {@code buffer} is transferred to this {@link HttpData}.
     *
     * @param buffer
     *            must be not null
     * @throws IOException
     */
    void SetContent(ByteBuf buffer);

    /**
     * Add the content from the ChannelBuffer
     * <p>{@link ByteBuf#release()} ownership of {@code buffer} is transferred to this {@link HttpData}.
     *
     * @param buffer
     *            must be not null except if last is set to False
     * @param last
     *            True of the buffer is the last one
     * @throws IOException
     */
    void AddContent(ByteBuf buffer, bool last);

    /**
     * Set the content from the file (erase any previous data)
     *
     * @param file
     *            must be not null
     * @throws IOException
     */
    void SetContent(FileInfo file);

    /**
     * Set the content from the inputStream (erase any previous data)
     *
     * @param inputStream
     *            must be not null
     * @throws IOException
     */
    void SetContent(System.IO.Stream inputStream);

    /**
     *
     * @return True if the InterfaceHttpData is completed (all data are stored)
     */
    bool IsCompleted();

    /**
     * Returns the size in byte of the InterfaceHttpData
     *
     * @return the size of the InterfaceHttpData
     */
    long Length();

    /**
     * Returns the defined length of the HttpData.
     *
     * If no Content-Length is provided in the request, the defined length is
     * always 0 (whatever during decoding or in final state).
     *
     * If Content-Length is provided in the request, this is this given defined length.
     * This value does not change, whatever during decoding or in the final state.
     *
     * This method could be used for instance to know the amount of bytes transmitted for
     * one particular HttpData, for example one {@link FileUpload} or any known big {@link Attribute}.
     *
     * @return the defined length of the HttpData
     */
    long DefinedLength();

    /**
     * Deletes the underlying storage for a file item, including deleting any
     * associated temporary disk file.
     */
    void Delete();

    /**
     * Returns the contents of the file item as an array of bytes.<br>
     * Note: this method will allocate a lot of memory, if the data is currently stored on the file system.
     *
     * @return the contents of the file item as an array of bytes.
     * @throws IOException
     */
    byte[] Get();

    /**
     * Returns the content of the file item as a ByteBuf.<br>
     * Note: this method will allocate a lot of memory, if the data is currently stored on the file system.
     *
     * @return the content of the file item as a ByteBuf
     * @throws IOException
     */
    ByteBuf GetByteBuf();

    /**
     * Returns a ChannelBuffer for the content from the current position with at
     * most length read bytes, increasing the current position of the Bytes
     * read. Once it arrives at the end, it returns an EMPTY_BUFFER and it
     * resets the current position to 0.
     *
     * @return a ChannelBuffer for the content from the current position or an
     *         EMPTY_BUFFER if there is no more data to return
     */
    ByteBuf GetChunk(int length);

    /**
     * Returns the contents of the file item as a String, using the default
     * character encoding.
     *
     * @return the contents of the file item as a String, using the default
     *         character encoding.
     * @throws IOException
     */
    string GetString();

    /**
     * Returns the contents of the file item as a String, using the specified
     * charset.
     *
     * @param encoding
     *            the charset to use
     * @return the contents of the file item as a String, using the specified
     *         charset.
     * @throws IOException
     */
    string GetString(Encoding encoding);

    /**
     * Set the Charset passed by the browser if defined
     *
     * @param charset
     *            Charset to set - must be not null
     */
    void SetCharset(Encoding charset);

    /**
     * Returns the Charset passed by the browser or null if not defined.
     *
     * @return the Charset passed by the browser or null if not defined.
     */
    Encoding GetCharset();

    /**
     * A convenience getMethod to write an uploaded item to disk. If a previous one
     * exists, it will be deleted. Once this getMethod is called, if successful,
     * the new file will be out of the cleaner of the factory that creates the
     * original InterfaceHttpData object.
     *
     * @param dest
     *            destination file - must be not null
     * @return True if the write is successful
     * @throws IOException
     */
    bool RenameTo(FileInfo dest);

    /**
     * Provides a hint as to whether or not the file contents will be read from
     * memory.
     *
     * @return True if the file contents is in memory.
     */
    bool IsInMemory();

    /**
     *
     * @return the associated File if this data is represented in a file
     * @exception IOException
     *                if this data is not represented by a file
     */
    FileInfo GetFile();
}
