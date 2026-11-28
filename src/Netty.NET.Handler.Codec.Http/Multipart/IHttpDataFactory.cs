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

using System.Text;

namespace Netty.NET.Handler.Codec.Http.Multipart;

/**
 * Interface to enable creation of InterfaceHttpData objects
 */
// CLR: The @Deprecated cleanRequestHttpDatas(HttpRequest) and cleanAllHttpDatas(), whose @deprecated texts name
// cleanRequestHttpData(HttpRequest) and cleanAllHttpData(), are not ported.
public interface IHttpDataFactory
{

    /**
     * To set a max size limitation on fields. Exceeding it will generate an ErrorDataDecoderException.
     * A value of -1 means no limitation (default).
     */
    void SetMaxLimit(long max);

    /**
     *
     * @param request associated request
     * @return a new Attribute with no value
     */
    IAttribute CreateAttribute(IHttpRequest request, string name);

    /**
     * @param request associated request
     * @param name name of the attribute
     * @param definedSize defined size from request for this attribute
     * @return a new Attribute with no value
     */
    IAttribute CreateAttribute(IHttpRequest request, string name, long definedSize);

    /**
     * @param request associated request
     * @return a new Attribute
     */
    IAttribute CreateAttribute(IHttpRequest request, string name, string value);

    /**
     * @param request associated request
     * @param size the size of the Uploaded file
     * @return a new FileUpload
     */
    IFileUpload CreateFileUpload(IHttpRequest request, string name, string filename,
                                 string contentType, string contentTransferEncoding, Encoding charset,
                                 long size);

    /**
     * Remove the given InterfaceHttpData from clean list (will not delete the file, except if the file
     * is still a temporary one as setup at construction)
     * @param request associated request
     */
    void RemoveHttpDataFromClean(IHttpRequest request, IInterfaceHttpData data);

    /**
     * Remove all InterfaceHttpData from virtual File storage from clean list for the request
     *
     * @param request associated request
     */
    void CleanRequestHttpData(IHttpRequest request);

    /**
     * Remove all InterfaceHttpData from virtual File storage from clean list for all requests
     */
    void CleanAllHttpData();
}
