// CLR: test-only stand-in for org.apache.mina.filter.codec.ProtocolCodecFilter of Apache MINA 2.2.3:
// https://github.com/apache/mina/blob/2.2.3/mina-core/src/main/java/org/apache/mina/filter/codec/ProtocolCodecFilter.java
// Kept: the factory constructor; messageReceived passes a non-IoBuffer message on, asks the factory for a decoder per
// buffer, decodes while the buffer has bytes left, flushes the decoded messages to the next filter after each decode
// and, when decode throws, flushes what was decoded, fires exceptionCaught (DnsProtocolHandler then closes the
// session, so no response is sent) and stops; filterWrite passes an IoBuffer on, asks the factory for an encoder per
// write, and writes every encoded message, or an empty buffer when the encoder wrote nothing (TestDnsServer's encoder
// writes nothing when filterMessage returns null, so no datagram is sent). Not reproduced: the
// ProtocolDecoderException/ProtocolEncoderException wrappers with their hex dump (the handler only logs the cause and
// closes the session), the RecoverableProtocolDecoderException retry (no decoder here throws one), the thread-local
// outputs (one output per call) and the constructors taking encoder/decoder instances or classes, which no caller
// uses.

using System;
using System.Collections.Generic;

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

/**
 * An {@link IoFilter} which translates binary or protocol specific data into
 * message objects and vice versa using {@link ProtocolCodecFactory},
 * {@link ProtocolEncoder}, or {@link ProtocolDecoder}.
 */
public class ProtocolCodecFilter : IIoFilter
{
    /** The factory responsible for creating the encoder and decoder */
    private readonly IProtocolCodecFactory factory;

    /**
     * Creates a new instance of ProtocolCodecFilter, associating a factory
     * for the creation of the encoder and decoder.
     *
     * @param factory The associated factory
     */
    public ProtocolCodecFilter(IProtocolCodecFactory factory)
    {
        if (factory == null)
        {
            throw new ArgumentNullException(nameof(factory), "factory");
        }

        this.factory = factory;
    }

    public virtual void MessageReceived(IIoFilter.INextFilter nextFilter, IIoSession session, object message)
    {
        if (!(message is IoBuffer))
        {
            nextFilter.MessageReceived(session, message);
            return;
        }

        IoBuffer @in = (IoBuffer)message;
        IProtocolDecoder decoder = factory.GetDecoder(session);
        ProtocolDecoderOutputImpl decoderOut = new ProtocolDecoderOutputImpl();

        // Loop until we don't have anymore byte in the buffer,
        // or until the decoder throws an unrecoverable exception or
        // can't decoder a message, because there are not enough
        // data in the buffer
        while (@in.HasRemaining())
        {
            try
            {
                // Call the decoder with the read bytes
                decoder.Decode(session, @in, decoderOut);
                // Finish decoding if no exception was thrown.
                decoderOut.Flush(nextFilter, session);
            }
            catch (Exception e)
            {
                // Fire the exceptionCaught event.
                decoderOut.Flush(nextFilter, session);
                nextFilter.ExceptionCaught(session, e);
                // CLR: MINA retries only after a RecoverableProtocolDecoderException that moved the position.
                break;
            }
        }
    }

    public virtual void FilterWrite(IIoFilter.INextFilter nextFilter, IIoSession session, object message)
    {
        // Bypass the encoding if the message is contained in a IoBuffer,
        // as it has already been encoded before
        if (message is IoBuffer)
        {
            nextFilter.FilterWrite(session, message);
            return;
        }

        // Get the encoder in the session
        IProtocolEncoder encoder = factory.GetEncoder(session);
        ProtocolEncoderOutputImpl encoderOut = new ProtocolEncoderOutputImpl();

        if (encoder == null)
        {
            throw new InvalidOperationException("The encoder is null for the session " + session);
        }

        // Now we can try to encode the response
        encoder.Encode(session, message, encoderOut);

        Queue<object> queue = encoderOut.messageQueue;

        if (queue.Count == 0)
        {
            // Write empty message to ensure that messageSent is fired later
            nextFilter.FilterWrite(session, IoBuffer.Allocate(0));
        }
        else
        {
            // Write all the encoded messages now
            while (queue.TryDequeue(out object encodedMessage))
            {
                nextFilter.FilterWrite(session, encodedMessage);
            }
        }
    }

    private sealed class ProtocolDecoderOutputImpl : IProtocolDecoderOutput
    {
        private readonly Queue<object> messageQueue = new Queue<object>();

        public void Write(object message)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message), "message");
            }

            messageQueue.Enqueue(message);
        }

        public void Flush(IIoFilter.INextFilter nextFilter, IIoSession session)
        {
            while (messageQueue.TryDequeue(out object decodedMessage))
            {
                nextFilter.MessageReceived(session, decodedMessage);
            }
        }
    }

    private sealed class ProtocolEncoderOutputImpl : IProtocolEncoderOutput
    {
        internal readonly Queue<object> messageQueue = new Queue<object>();

        public void Write(object encodedMessage)
        {
            if (encodedMessage == null)
            {
                throw new ArgumentNullException(nameof(encodedMessage), "encodedMessage");
            }

            messageQueue.Enqueue(encodedMessage);
        }
    }
}
