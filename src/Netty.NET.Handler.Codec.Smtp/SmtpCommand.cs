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
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Smtp;

/**
 * The command part of a {@link SmtpRequest}.
 */
// CLR: @UnstableApi is the Common [UnstableApi] attribute; the static initializer is the static constructor, which runs
// after the static field initializers. CharSequence is ICharSequence (see SmtpUtils); HashMap<String, SmtpCommand> is
// Dictionary<string, SmtpCommand>, read with GetValueOrDefault. name() stays a method; the package-private encode and
// isContentExpected are internal; hashCode()/equals(Object)/toString() are the BCL GetHashCode()/Equals(object)/
// ToString(). checkNotNull throws ArgumentNullException (docs/codec-base-porting.md).
[UnstableApi]
public sealed class SmtpCommand
{
    public static readonly SmtpCommand EHLO = new SmtpCommand(AsciiString.Cached("EHLO"));
    public static readonly SmtpCommand HELO = new SmtpCommand(AsciiString.Cached("HELO"));
    public static readonly SmtpCommand AUTH = new SmtpCommand(AsciiString.Cached("AUTH"));
    public static readonly SmtpCommand MAIL = new SmtpCommand(AsciiString.Cached("MAIL"));
    public static readonly SmtpCommand RCPT = new SmtpCommand(AsciiString.Cached("RCPT"));
    public static readonly SmtpCommand DATA = new SmtpCommand(AsciiString.Cached("DATA"));
    public static readonly SmtpCommand NOOP = new SmtpCommand(AsciiString.Cached("NOOP"));
    public static readonly SmtpCommand RSET = new SmtpCommand(AsciiString.Cached("RSET"));
    public static readonly SmtpCommand EXPN = new SmtpCommand(AsciiString.Cached("EXPN"));
    public static readonly SmtpCommand VRFY = new SmtpCommand(AsciiString.Cached("VRFY"));
    public static readonly SmtpCommand HELP = new SmtpCommand(AsciiString.Cached("HELP"));
    public static readonly SmtpCommand QUIT = new SmtpCommand(AsciiString.Cached("QUIT"));
    public static readonly SmtpCommand EMPTY = new SmtpCommand(AsciiString.Cached(""));

    private static readonly Dictionary<string, SmtpCommand> COMMANDS = new Dictionary<string, SmtpCommand>();
    static SmtpCommand()
    {
        COMMANDS[EHLO.Name().ToString()] = EHLO;
        COMMANDS[HELO.Name().ToString()] = HELO;
        COMMANDS[AUTH.Name().ToString()] = AUTH;
        COMMANDS[MAIL.Name().ToString()] = MAIL;
        COMMANDS[RCPT.Name().ToString()] = RCPT;
        COMMANDS[DATA.Name().ToString()] = DATA;
        COMMANDS[NOOP.Name().ToString()] = NOOP;
        COMMANDS[RSET.Name().ToString()] = RSET;
        COMMANDS[EXPN.Name().ToString()] = EXPN;
        COMMANDS[VRFY.Name().ToString()] = VRFY;
        COMMANDS[HELP.Name().ToString()] = HELP;
        COMMANDS[QUIT.Name().ToString()] = QUIT;
        COMMANDS[EMPTY.Name().ToString()] = EMPTY;
    }

    /**
     * Returns the {@link SmtpCommand} for the given command name.
     */
    public static SmtpCommand ValueOf(ICharSequence commandName)
    {
        ObjectUtil.CheckNotNull(commandName, "commandName");

        SmtpCommand command = COMMANDS.GetValueOrDefault(commandName.ToString());
        return command != null ? command : new SmtpCommand(AsciiString.Of(commandName));
    }

    private readonly AsciiString name;

    private SmtpCommand(AsciiString name)
    {
        SmtpUtils.ValidateSMTPParameter(name);
        this.name = name;
    }

    /**
     * Return the command name.
     */
    public AsciiString Name()
    {
        return name;
    }

    internal void Encode(ByteBuf buffer)
    {
        ByteBufUtil.WriteAscii(buffer, name);
    }

    internal bool IsContentExpected()
    {
        return this.Equals(DATA);
    }

    public override int GetHashCode()
    {
        return name.GetHashCode();
    }

    public override bool Equals(object obj)
    {
        if (obj == this)
        {
            return true;
        }
        if (!(obj is SmtpCommand))
        {
            return false;
        }

        return name.ContentEqualsIgnoreCase(((SmtpCommand) obj).Name());
    }

    public override string ToString()
    {
        return "SmtpCommand{name=" + name + '}';
    }
}
