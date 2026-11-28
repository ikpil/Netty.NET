/*
 * Copyright 2021 The Netty Project
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

using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Quic;

/**
 * Configuration used for setup
 * <a href="https://quiclog.github.io/internet-drafts/draft-marx-qlog-main-schema.html">qlog</a>.
 */
// CLR: Objects.requireNonNull(x, name) is ObjectUtil.CheckNotNull (ArgumentNullException for
// NullPointerException). System.Net.Quic has no QLOG output (msquic traces through its own event tracing;
// docs/quic-design.md, No counterpart in System.Net.Quic): the type keeps its Java API and the msquic
// implementation (stage 2) reports QuicChannelOption.QLOG as unsupported.
public sealed class QLogConfiguration
{

    private readonly string path;
    private readonly string logTitle;
    private readonly string logDescription;

    /**
     * Create a new configuration.
     *
     * @param path              the path to the log file to use. This file must not exist yet. If the path is a
     *                          directory the filename will be generated
     * @param logTitle          the title to use when logging.
     * @param logDescription    the description to use when logging.
     */
    public QLogConfiguration(string path, string logTitle, string logDescription)
    {
        this.path = CheckNotNull(path, "path");
        this.logTitle = CheckNotNull(logTitle, "logTitle");
        this.logDescription = CheckNotNull(logDescription, "logDescription");
    }

    /**
     * Return the path to the log file.
     *
     * @return the path.
     */
    public string Path()
    {
        return path;
    }

    /**
     * Return the title.
     *
     * @return the title.
     */
    public string LogTitle()
    {
        return logTitle;
    }

    /**
     * Return the description.
     *
     * @return the description.
     */
    public string LogDescription()
    {
        return logDescription;
    }
}
