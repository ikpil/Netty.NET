/*
 * Copyright 2015 The Netty Project
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
using Netty.NET.Buffer;
using static Netty.NET.Buffer.Unpooled;
using static Netty.NET.Example.Http2.Http2ExampleUtil;

namespace Netty.NET.Example.Http2.Tiles;

/**
 * Caches the images to avoid reading them every time from the disk.
 */
// CLR: getClass().getResourceAsStream(name) resolves name against the class's package in the class path
// (io/netty/example/http2/tiles/). The tile images are copied byte for byte to
// src/Netty.NET.Example/Resources/io/netty/example/http2/tiles/ and embedded in the assembly under that Java resource
// name (EmbeddedResource with LogicalName io/netty/example/http2/tiles/%(Filename)%(Extension)), so the stream is
// Assembly.GetManifestResourceStream of the package path and the name, which returns null for a missing resource as
// getResourceAsStream does. Map/HashMap are IDictionary/Dictionary (imageBank.get of a missing name returns null, as
// Map.get); IOException is System.IO.IOException and e.printStackTrace() writes the exception to Console.Error.
public sealed class ImageCache
{

    public static ImageCache INSTANCE = new ImageCache();

    // CLR-only: the class path directory of the Java package, the prefix of the embedded resource names.
    private const string RESOURCE_PACKAGE = "io/netty/example/http2/tiles/";

    private readonly IDictionary<string, ByteBuf> imageBank = new Dictionary<string, ByteBuf>(200);

    private ImageCache()
    {
        Init();
    }

    public static string Name(int x, int y)
    {
        return "tile-" + y + "-" + x + ".jpeg";
    }

    public ByteBuf Image(int x, int y)
    {
        return imageBank.TryGetValue(Name(x, y), out ByteBuf image) ? image : null;
    }

    private void Init()
    {
        for (int y = 0; y < 10; y++)
        {
            for (int x = 0; x < 20; x++)
            {
                string name = Name(x, y);
                try
                {
                    using (Stream resourceAsStream = GetType().Assembly.GetManifestResourceStream(RESOURCE_PACKAGE + name))
                    {
                        ByteBuf fileBytes = UnreleasableBuffer(ToByteBuf(resourceAsStream).AsReadOnly());
                        imageBank[name] = fileBytes;
                    }
                }
                catch (IOException e)
                {
                    Console.Error.WriteLine(e);
                }
            }
        }
    }
}
