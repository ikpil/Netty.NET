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

namespace Netty.NET.Handler.Codec.Http.Multipart;

/**
 * FileUpload interface that could be in memory, on temporary file or any other implementations.
 *
 * Most methods are inspired from java.io.File API.
 */
// CLR: the Java interface FileUpload takes the I prefix (owner rule). copy(), duplicate(), retainedDuplicate(),
// replace(ByteBuf), retain(), retain(int), touch() and touch(Object) are re-declared only to narrow the return
// type, so they are inherited from IByteBufHolder; the classes narrow them with covariant override returns.
public interface IFileUpload : IHttpData
{
    /**
     * Returns the original filename in the client's filesystem,
     * as provided by the browser (or other client software).
     * @return the original filename
     */
    string GetFilename();

    /**
     * Set the original filename.
     * <p>
     * <strong>Note:</strong> This method validates that the filename is safe for including in an HTTP request,
     * and will throw an exception if that's not the case.
     */
    void SetFilename(string filename);

    /**
     * Set the Content Type passed by the browser if defined
     * @param contentType Content Type to set - must be not null
     */
    void SetContentType(string contentType);

    /**
     * Returns the content type passed by the browser or null if not defined.
     * @return the content type passed by the browser or null if not defined.
     */
    string GetContentType();

    /**
     * Set the Content-Transfer-Encoding type from String as 7bit, 8bit or binary
     */
    void SetContentTransferEncoding(string contentTransferEncoding);

    /**
     * Returns the Content-Transfer-Encoding
     * @return the Content-Transfer-Encoding
     */
    string GetContentTransferEncoding();
}
