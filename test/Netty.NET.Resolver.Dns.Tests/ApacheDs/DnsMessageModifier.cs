// CLR: test-only stand-in for org.apache.directory.server.dns.messages.DnsMessageModifier of apacheds-protocol-dns
// 2.0.0.AM27:
// https://github.com/apache/directory-server/blob/2.0.0.AM27/protocol-dns/src/main/java/org/apache/directory/server/dns/messages/DnsMessageModifier.java
// The record lists are IList<QuestionRecord> and IList<IResourceRecord>, passed by reference to the DnsMessage that
// getDnsMessage() builds, as in the source. The setters stay methods.

using System.Collections.Generic;

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

/**
 * All communications inside of the domain protocol are carried in a single
 * format called a message.  The top level format of message is divided
 * into 5 sections (some of which are empty in certain cases) shown below:
 *
 *     +---------------------+
 *     |        Header       |
 *     +---------------------+
 *     |       Question      | the question for the name server
 *     +---------------------+
 *     |        Answer       | ResourceRecords answering the question
 *     +---------------------+
 *     |      Authority      | ResourceRecords pointing toward an authority
 *     +---------------------+
 *     |      Additional     | ResourceRecords holding additional information
 *     +---------------------+
 */
public class DnsMessageModifier
{
    /**
     * The header section is always present.  The header includes fields that
     * specify which of the remaining sections are present, and also specify
     * whether the message is a query or a response, a standard query or some
     * other opcode, etc.
     */
    private int transactionId;
    private MessageType messageType;
    private OpCode opCode;
    private bool authoritativeAnswer;
    private bool truncated;
    private bool recursionDesired;
    private bool recursionAvailable;
    private bool reserved;
    private bool acceptNonAuthenticatedData;

    private ResponseCode responseCode;
    private IList<QuestionRecord> questionRecords;
    private IList<IResourceRecord> answerRecords;
    private IList<IResourceRecord> authorityRecords;
    private IList<IResourceRecord> additionalRecords;

    /**
     * Returns the {@link DnsMessage}.
     *
     * @return The {@link DnsMessage}.
     */
    public DnsMessage GetDnsMessage()
    {
        return new DnsMessage(transactionId, messageType, opCode, authoritativeAnswer, truncated, recursionDesired,
            recursionAvailable, reserved, acceptNonAuthenticatedData, responseCode, questionRecords, answerRecords,
            authorityRecords, additionalRecords);
    }

    /**
     * @param acceptNonAuthenticatedData The acceptNonAuthenticatedData to set.
     */
    public void SetAcceptNonAuthenticatedData(bool acceptNonAuthenticatedData)
    {
        this.acceptNonAuthenticatedData = acceptNonAuthenticatedData;
    }

    /**
     * @param additionalRecords The additional to set.
     */
    public void SetAdditionalRecords(IList<IResourceRecord> additionalRecords)
    {
        this.additionalRecords = additionalRecords;
    }

    /**
     * @param answerRecords The answer to set.
     */
    public void SetAnswerRecords(IList<IResourceRecord> answerRecords)
    {
        this.answerRecords = answerRecords;
    }

    /**
     * @param authoritativeAnswer The authoritativeAnswer to set.
     */
    public void SetAuthoritativeAnswer(bool authoritativeAnswer)
    {
        this.authoritativeAnswer = authoritativeAnswer;
    }

    /**
     * @param authorityRecords The authority to set.
     */
    public void SetAuthorityRecords(IList<IResourceRecord> authorityRecords)
    {
        this.authorityRecords = authorityRecords;
    }

    /**
     * @param messageType The messageType to set.
     */
    public void SetMessageType(MessageType messageType)
    {
        this.messageType = messageType;
    }

    /**
     * @param opCode The opCode to set.
     */
    public void SetOpCode(OpCode opCode)
    {
        this.opCode = opCode;
    }

    /**
     * @param questionRecords The question to set.
     */
    public void SetQuestionRecords(IList<QuestionRecord> questionRecords)
    {
        this.questionRecords = questionRecords;
    }

    /**
     * @param recursionAvailable The recursionAvailable to set.
     */
    public void SetRecursionAvailable(bool recursionAvailable)
    {
        this.recursionAvailable = recursionAvailable;
    }

    /**
     * @param recursionDesired The recursionDesired to set.
     */
    public void SetRecursionDesired(bool recursionDesired)
    {
        this.recursionDesired = recursionDesired;
    }

    /**
     * @param reserved The reserved to set.
     */
    public void SetReserved(bool reserved)
    {
        this.reserved = reserved;
    }

    /**
     * @param responseCode The responseCode to set.
     */
    public void SetResponseCode(ResponseCode responseCode)
    {
        this.responseCode = responseCode;
    }

    /**
     * @param transactionId The transactionId to set.
     */
    public void SetTransactionId(int transactionId)
    {
        this.transactionId = transactionId;
    }

    /**
     * @param truncated The truncated to set.
     */
    public void SetTruncated(bool truncated)
    {
        this.truncated = truncated;
    }
}
