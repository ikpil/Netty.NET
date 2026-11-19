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

using System.Collections.Generic;
using System.Collections.ObjectModel;
using Netty.NET.Common;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Smtp;

/**
 * Default {@link SmtpRequest} implementation.
 */
// CLR: @UnstableApi is the Common [UnstableApi] attribute; CharSequence... is params ICharSequence[] and List<CharSequence>
// is IList<ICharSequence> (see SmtpUtils). Collections.emptyList() is ReadOnlyCollection<T>.Empty and
// Collections.unmodifiableList(list) is a ReadOnlyCollection<T> view of the list. The List equals/hashCode/toString
// used by the source go through SmtpUtils.ListEquals/ListHashCode/ListToString, since ReadOnlyCollection<T> keeps
// Object's members. The package-private constructor is internal; checkNotNull throws ArgumentNullException
// (docs/codec-base-porting.md).
[UnstableApi]
public sealed class DefaultSmtpRequest : ISmtpRequest
{

    private readonly SmtpCommand command;
    private readonly IList<ICharSequence> parameters;

    /**
     * Creates a new instance with the given command and no parameters.
     */
    public DefaultSmtpRequest(SmtpCommand command)
    {
        this.command = ObjectUtil.CheckNotNull(command, "command");
        parameters = ReadOnlyCollection<ICharSequence>.Empty;
    }

    /**
     * Creates a new instance with the given command and parameters.
     */
    public DefaultSmtpRequest(SmtpCommand command, params ICharSequence[] parameters)
    {
        this.command = ObjectUtil.CheckNotNull(command, "command");
        SmtpUtils.ValidateSMTPParameters(parameters);
        this.parameters = SmtpUtils.ToUnmodifiableList(parameters);
    }

    /**
     * Creates a new instance with the given command and parameters.
     */
    public DefaultSmtpRequest(ICharSequence command, params ICharSequence[] parameters) :
        this(SmtpCommand.ValueOf(command), parameters)
    {
    }

    internal DefaultSmtpRequest(SmtpCommand command, IList<ICharSequence> parameters)
    {
        this.command = ObjectUtil.CheckNotNull(command, "command");
        SmtpUtils.ValidateSMTPParameters(parameters);
        this.parameters = parameters != null ?
                new ReadOnlyCollection<ICharSequence>(parameters) : ReadOnlyCollection<ICharSequence>.Empty;
    }

    public SmtpCommand Command()
    {
        return command;
    }

    public IList<ICharSequence> Parameters()
    {
        return parameters;
    }

    public override int GetHashCode()
    {
        return command.GetHashCode() * 31 + SmtpUtils.ListHashCode(parameters);
    }

    public override bool Equals(object o)
    {
        if (!(o is DefaultSmtpRequest))
        {
            return false;
        }

        if (o == this)
        {
            return true;
        }

        DefaultSmtpRequest other = (DefaultSmtpRequest) o;

        return Command().Equals(other.Command()) &&
                SmtpUtils.ListEquals(Parameters(), other.Parameters());
    }

    public override string ToString()
    {
        return "DefaultSmtpRequest{" +
                "command=" + command +
                ", parameters=" + SmtpUtils.ListToString(parameters) +
                '}';
    }
}
