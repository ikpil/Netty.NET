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
using Netty.NET.Common;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Smtp;

/**
 * Provides utility methods to create {@link SmtpRequest}s.
 */
// CLR: @UnstableApi is the Common [UnstableApi] attribute; the final utility class with a private constructor is a
// static class. CharSequence... is params ICharSequence[] (see SmtpUtils); the String results of the source
// concatenations and help(String) are passed as their ICharSequence wrapper StringCharSequence, and the conditional
// between a StringCharSequence and the AsciiString FROM_NULL_SENDER is typed ICharSequence explicitly.
// Collections.addAll(list, array) is List<T>.AddRange; the local 'params' is @params (a C# keyword). checkNotNull
// throws ArgumentNullException (docs/codec-base-porting.md).
[UnstableApi]
public static class SmtpRequests
{

    private static readonly ISmtpRequest DATA = new DefaultSmtpRequest(SmtpCommand.DATA);
    private static readonly ISmtpRequest NOOP = new DefaultSmtpRequest(SmtpCommand.NOOP);
    private static readonly ISmtpRequest RSET = new DefaultSmtpRequest(SmtpCommand.RSET);
    private static readonly ISmtpRequest HELP_NO_ARG = new DefaultSmtpRequest(SmtpCommand.HELP);
    private static readonly ISmtpRequest QUIT = new DefaultSmtpRequest(SmtpCommand.QUIT);
    private static readonly AsciiString FROM_NULL_SENDER = AsciiString.Cached("FROM:<>");

    /**
     * Creates a {@code HELO} request.
     */
    public static ISmtpRequest Helo(ICharSequence hostname)
    {
        return new DefaultSmtpRequest(SmtpCommand.HELO, hostname);
    }

    /**
     * Creates a {@code EHLO} request.
     */
    public static ISmtpRequest Ehlo(ICharSequence hostname)
    {
        return new DefaultSmtpRequest(SmtpCommand.EHLO, hostname);
    }

    /**
     * Creates a {@code EMPTY} request.
     */
    public static ISmtpRequest Empty(params ICharSequence[] parameter)
    {
        return new DefaultSmtpRequest(SmtpCommand.EMPTY, parameter);
    }

    /**
     * Creates a {@code AUTH} request.
     */
    public static ISmtpRequest Auth(params ICharSequence[] parameter)
    {
        return new DefaultSmtpRequest(SmtpCommand.AUTH, parameter);
    }

    /**
     * Creates a {@code NOOP} request.
     */
    public static ISmtpRequest Noop()
    {
        return NOOP;
    }

    /**
     * Creates a {@code DATA} request.
     */
    public static ISmtpRequest Data()
    {
        return DATA;
    }

    /**
     * Creates a {@code RSET} request.
     */
    public static ISmtpRequest Rset()
    {
        return RSET;
    }

    /**
     * Creates a {@code HELP} request.
     */
    public static ISmtpRequest Help(string cmd)
    {
        return cmd == null ? HELP_NO_ARG : new DefaultSmtpRequest(SmtpCommand.HELP, new StringCharSequence(cmd));
    }

    /**
     * Creates a {@code QUIT} request.
     */
    public static ISmtpRequest Quit()
    {
        return QUIT;
    }

    /**
     * Creates a {@code MAIL} request.
     */
    public static ISmtpRequest Mail(ICharSequence sender, params ICharSequence[] mailParameters)
    {
        if (mailParameters == null || mailParameters.Length == 0)
        {
            return new DefaultSmtpRequest(SmtpCommand.MAIL,
                                          sender != null ? new StringCharSequence("FROM:<" + sender + '>') : (ICharSequence) FROM_NULL_SENDER);
        }
        else
        {
            List<ICharSequence> @params = new List<ICharSequence>(mailParameters.Length + 1);
            @params.Add(sender != null? new StringCharSequence("FROM:<" + sender + '>') : (ICharSequence) FROM_NULL_SENDER);
            @params.AddRange(mailParameters);
            return new DefaultSmtpRequest(SmtpCommand.MAIL, @params);
        }
    }

    /**
     * Creates a {@code RCPT} request.
     */
    public static ISmtpRequest Rcpt(ICharSequence recipient, params ICharSequence[] rcptParameters)
    {
        ObjectUtil.CheckNotNull(recipient, "recipient");
        if (rcptParameters == null || rcptParameters.Length == 0)
        {
            return new DefaultSmtpRequest(SmtpCommand.RCPT, new StringCharSequence("TO:<" + recipient + '>'));
        }
        else
        {
            List<ICharSequence> @params = new List<ICharSequence>(rcptParameters.Length + 1);
            @params.Add(new StringCharSequence("TO:<" + recipient + '>'));
            @params.AddRange(rcptParameters);
            return new DefaultSmtpRequest(SmtpCommand.RCPT, @params);
        }
    }

    /**
     * Creates a {@code EXPN} request.
     */
    public static ISmtpRequest Expn(ICharSequence mailingList)
    {
        return new DefaultSmtpRequest(SmtpCommand.EXPN, ObjectUtil.CheckNotNull(mailingList, "mailingList"));
    }

    /**
     * Creates a {@code VRFY} request.
     */
    public static ISmtpRequest Vrfy(ICharSequence user)
    {
        return new DefaultSmtpRequest(SmtpCommand.VRFY, ObjectUtil.CheckNotNull(user, "user"));
    }
}
