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
using System.IO;
using System.Text;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Http.Multipart;

/**
 * Default factory giving {@link Attribute} and {@link FileUpload} according to constructor.
 *
 * <p>According to the constructor, {@link Attribute} and {@link FileUpload} can be:</p>
 * <ul>
 * <li>MemoryAttribute, DiskAttribute or MixedAttribute</li>
 * <li>MemoryFileUpload, DiskFileUpload or MixedFileUpload</li>
 * </ul>
 * A good example of releasing HttpData once all work is done is as follow:<br>
 * <pre>{@code
 *   for (InterfaceHttpData httpData: decoder.getBodyHttpDatas()) {
 *     httpData.release();
 *     factory.removeHttpDataFromClean(request, httpData);
 *   }
 *   factory.cleanAllHttpData();
 *   decoder.destroy();
 *  }</pre>
 */
// CLR: Charset is Encoding and HttpRequest IHttpRequest. Collections.synchronizedMap(new IdentityHashMap<>()), a map
// that uses "reference-equality in place of object-equality when comparing keys"
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/IdentityHashMap.html) and serializes every
// access (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/Collections.html), is a Dictionary with
// ReferenceEqualityComparer.Instance, which "uses reference equality ... instead of value equality"
// (https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.referenceequalitycomparer?view=net-10.0),
// guarded by lock (requestFileDeleteMap) in each method; the traversal of cleanAllHttpData, which the Java code does not
// synchronize, runs under the same lock over a snapshot of the entries, so removing an entry does not disturb it.
// map.get() returning null is TryGetValue. IOException is System.IO.IOException and IllegalArgumentException is
// ArgumentException (new IllegalArgumentException(e) keeps Java's detail message cause.toString(), Common's
// ThrowableUtil.ThrowableToString). The last MemoryAttribute/MemoryFileUpload locals of createAttribute(request,
// name[, definedSize]) and createFileUpload are named memoryAttribute/memoryFileUpload, because a C# local's scope is
// its whole block and the if blocks before declare the same names (CS0136). The overrides of the @Deprecated
// cleanRequestHttpDatas(HttpRequest) and cleanAllHttpDatas() are not ported with the interface members (see
// IHttpDataFactory).
public class DefaultHttpDataFactory : IHttpDataFactory
{

    /**
     * Proposed default MINSIZE as 16 KB.
     */
    public const long MINSIZE = 0x4000;
    /**
     * Proposed default MAXSIZE = -1 as UNLIMITED
     */
    public const long MAXSIZE = -1;

    private readonly bool useDisk;

    private readonly bool checkSize;

    private long minSize;

    private long maxSize = MAXSIZE;

    private Encoding charset = HttpConstants.DEFAULT_CHARSET;

    private string baseDir;

    private bool deleteOnExit; // false is a good default cause true leaks

    /**
     * Keep all {@link HttpData}s until cleaning methods are called.
     * We need to use {@link IdentityHashMap} because different requests may be equal.
     * See {@link DefaultHttpRequest#hashCode} and {@link DefaultHttpRequest#equals}.
     * Similarly, when removing data items, we need to check their identities because
     * different data items may be equal.
     */
    private readonly Dictionary<IHttpRequest, List<IHttpData>> requestFileDeleteMap =
            new Dictionary<IHttpRequest, List<IHttpData>>(ReferenceEqualityComparer.Instance);

    /**
     * HttpData will be in memory if less than default size (16KB).
     * The type will be Mixed.
     */
    public DefaultHttpDataFactory()
    {
        useDisk = false;
        checkSize = true;
        minSize = MINSIZE;
    }

    public DefaultHttpDataFactory(Encoding charset) : this()
    {
        this.charset = charset;
    }

    /**
     * HttpData will be always on Disk if useDisk is True, else always in Memory if False
     */
    public DefaultHttpDataFactory(bool useDisk)
    {
        this.useDisk = useDisk;
        checkSize = false;
    }

    public DefaultHttpDataFactory(bool useDisk, Encoding charset) : this(useDisk)
    {
        this.charset = charset;
    }

    /**
     * HttpData will be on Disk if the size of the file is greater than minSize, else it
     * will be in memory. The type will be Mixed.
     */
    public DefaultHttpDataFactory(long minSize)
    {
        useDisk = false;
        checkSize = true;
        this.minSize = minSize;
    }

    public DefaultHttpDataFactory(long minSize, Encoding charset) : this(minSize)
    {
        this.charset = charset;
    }

    /**
     * Override global {@link DiskAttribute#baseDirectory} and {@link DiskFileUpload#baseDirectory} values.
     *
     * @param baseDir directory path where to store disk attributes and file uploads.
     */
    public virtual void SetBaseDir(string baseDir)
    {
        this.baseDir = baseDir;
    }

    /**
     * Override global {@link DiskAttribute#deleteOnExitTemporaryFile} and
     * {@link DiskFileUpload#deleteOnExitTemporaryFile} values.
     *
     * @param deleteOnExit true if temporary files should be deleted with the JVM, false otherwise.
     */
    public virtual void SetDeleteOnExit(bool deleteOnExit)
    {
        this.deleteOnExit = deleteOnExit;
    }

    public virtual void SetMaxLimit(long maxSize)
    {
        this.maxSize = maxSize;
    }

    /**
     * @return the associated list of {@link HttpData} for the request
     */
    private List<IHttpData> GetList(IHttpRequest request)
    {
        lock (requestFileDeleteMap)
        {
            if (!requestFileDeleteMap.TryGetValue(request, out List<IHttpData> list))
            {
                list = new List<IHttpData>();
                requestFileDeleteMap[request] = list;
            }

            return list;
        }
    }

    public virtual IAttribute CreateAttribute(IHttpRequest request, string name)
    {
        if (useDisk)
        {
            IAttribute attribute = new DiskAttribute(name, charset, baseDir, deleteOnExit);
            attribute.SetMaxSize(maxSize);
            List<IHttpData> list = GetList(request);
            list.Add(attribute);
            return attribute;
        }

        if (checkSize)
        {
            IAttribute attribute = new MixedAttribute(name, minSize, charset, baseDir, deleteOnExit);
            attribute.SetMaxSize(maxSize);
            List<IHttpData> list = GetList(request);
            list.Add(attribute);
            return attribute;
        }

        MemoryAttribute memoryAttribute = new MemoryAttribute(name);
        memoryAttribute.SetMaxSize(maxSize);
        return memoryAttribute;
    }

    public virtual IAttribute CreateAttribute(IHttpRequest request, string name, long definedSize)
    {
        if (useDisk)
        {
            IAttribute attribute = new DiskAttribute(name, definedSize, charset, baseDir, deleteOnExit);
            attribute.SetMaxSize(maxSize);
            List<IHttpData> list = GetList(request);
            list.Add(attribute);
            return attribute;
        }

        if (checkSize)
        {
            IAttribute attribute = new MixedAttribute(name, definedSize, minSize, charset, baseDir, deleteOnExit);
            attribute.SetMaxSize(maxSize);
            List<IHttpData> list = GetList(request);
            list.Add(attribute);
            return attribute;
        }

        MemoryAttribute memoryAttribute = new MemoryAttribute(name, definedSize);
        memoryAttribute.SetMaxSize(maxSize);
        return memoryAttribute;
    }

    /**
     * Utility method
     */
    private static void CheckHttpDataSize(IHttpData data)
    {
        try
        {
            data.CheckSize(data.Length());
        }
        catch (IOException)
        {
            throw new ArgumentException("Attribute bigger than maxSize allowed");
        }
    }

    public virtual IAttribute CreateAttribute(IHttpRequest request, string name, string value)
    {
        if (useDisk)
        {
            IAttribute attribute;

            try
            {
                attribute = new DiskAttribute(name, value, charset, baseDir, deleteOnExit);
                attribute.SetMaxSize(maxSize);
            }
            catch (IOException)
            {
                // revert to Mixed mode
                attribute = new MixedAttribute(name, value, minSize, charset, baseDir, deleteOnExit);
                attribute.SetMaxSize(maxSize);
            }

            CheckHttpDataSize(attribute);
            List<IHttpData> list = GetList(request);
            list.Add(attribute);
            return attribute;
        }

        if (checkSize)
        {
            IAttribute attribute = new MixedAttribute(name, value, minSize, charset, baseDir, deleteOnExit);
            attribute.SetMaxSize(maxSize);
            CheckHttpDataSize(attribute);
            List<IHttpData> list = GetList(request);
            list.Add(attribute);
            return attribute;
        }

        try
        {
            MemoryAttribute attribute = new MemoryAttribute(name, value, charset);
            attribute.SetMaxSize(maxSize);
            CheckHttpDataSize(attribute);
            return attribute;
        }
        catch (IOException e)
        {
            throw new ArgumentException(ThrowableUtil.ThrowableToString(e), e);
        }
    }

    public virtual IFileUpload CreateFileUpload(IHttpRequest request, string name, string filename,
            string contentType, string contentTransferEncoding, Encoding charset,
            long size)
    {
        if (useDisk)
        {
            IFileUpload fileUpload = new DiskFileUpload(name, filename, contentType,
                    contentTransferEncoding, charset, size, baseDir, deleteOnExit);
            fileUpload.SetMaxSize(maxSize);
            CheckHttpDataSize(fileUpload);
            List<IHttpData> list = GetList(request);
            list.Add(fileUpload);
            return fileUpload;
        }

        if (checkSize)
        {
            IFileUpload fileUpload = new MixedFileUpload(name, filename, contentType,
                    contentTransferEncoding, charset, size, minSize, baseDir, deleteOnExit);
            fileUpload.SetMaxSize(maxSize);
            CheckHttpDataSize(fileUpload);
            List<IHttpData> list = GetList(request);
            list.Add(fileUpload);
            return fileUpload;
        }

        MemoryFileUpload memoryFileUpload = new MemoryFileUpload(name, filename, contentType,
                contentTransferEncoding, charset, size);
        memoryFileUpload.SetMaxSize(maxSize);
        CheckHttpDataSize(memoryFileUpload);
        return memoryFileUpload;
    }

    public virtual void RemoveHttpDataFromClean(IHttpRequest request, IInterfaceHttpData data)
    {
        if (!(data is IHttpData))
        {
            return;
        }

        lock (requestFileDeleteMap)
        {
            // Do not use getList because it adds empty list to requestFileDeleteMap
            // if request is not found
            if (!requestFileDeleteMap.TryGetValue(request, out List<IHttpData> list))
            {
                return;
            }

            // Can't simply call list.remove(data), because different data items may be equal.
            // Need to check identity.
            for (int i = 0; i < list.Count; i++)
            {
                IHttpData n = list[i];
                if (ReferenceEquals(n, data))
                {
                    list.RemoveAt(i);

                    // Remove empty list to avoid memory leak
                    if (list.Count == 0)
                    {
                        requestFileDeleteMap.Remove(request);
                    }

                    return;
                }
            }
        }
    }

    public virtual void CleanRequestHttpData(IHttpRequest request)
    {
        List<IHttpData> list;

        lock (requestFileDeleteMap)
        {
            requestFileDeleteMap.Remove(request, out list);
        }

        if (list != null)
        {
            foreach (IHttpData data in list)
            {
                data.Release();
            }
        }
    }

    public virtual void CleanAllHttpData()
    {
        lock (requestFileDeleteMap)
        {
            foreach (KeyValuePair<IHttpRequest, List<IHttpData>> e in
                     new List<KeyValuePair<IHttpRequest, List<IHttpData>>>(requestFileDeleteMap))
            {

                // Calling i.remove() here will cause "java.lang.IllegalStateException: Entry was removed"
                // at e.getValue() below

                List<IHttpData> list = e.Value;

                foreach (IHttpData data in list)
                {
                    data.Release();
                }

                requestFileDeleteMap.Remove(e.Key);
            }
        }
    }
}
