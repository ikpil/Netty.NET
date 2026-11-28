// CLR: test-only stand-in for org.apache.directory.server.dns.messages.DnsMessage of apacheds-protocol-dns 2.0.0.AM27:
// https://github.com/apache/directory-server/blob/2.0.0.AM27/protocol-dns/src/main/java/org/apache/directory/server/dns/messages/DnsMessage.java
// List<QuestionRecord> and List<ResourceRecord> are IList<QuestionRecord> and IList<IResourceRecord>, kept by
// reference: getAuthorityRecords().add(...) in a TestDnsServer.filterMessage override changes the message that is then
// encoded, as in the source (DomainNameService builds the reply with fresh ArrayLists). The getters stay methods.
// equals/hashCode (commons-lang EqualsBuilder/HashCodeBuilder over every field) are not reproduced: no caller compares
// messages, so the CLR reference equality stands. toString keeps the field order of the source's ToStringBuilder
// without its identity prefix.

using System.Collections.Generic;
using System.Globalization;
using System.Text;

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
public class DnsMessage
{
    /**
     * The header section is always present.  The header includes fields that
     * specify which of the remaining sections are present, and also specify
     * whether the message is a query or a response, a standard query or some
     * other opcode, etc.
     */
    private readonly int transactionId;
    private readonly MessageType messageType;
    private readonly OpCode opCode;
    private readonly bool authoritativeAnswer;
    private readonly bool truncated;
    private readonly bool recursionDesired;
    private readonly bool recursionAvailable;
    private readonly bool reserved;
    private readonly bool acceptNonAuthenticatedData;

    private readonly ResponseCode responseCode;
    private readonly IList<QuestionRecord> questionRecords;
    private readonly IList<IResourceRecord> answerRecords;
    private readonly IList<IResourceRecord> authorityRecords;
    private readonly IList<IResourceRecord> additionalRecords;

    /**
     * Creates a new instance of DnsMessage.
     *
     * @param transactionId
     * @param messageType
     * @param opCode
     * @param authoritativeAnswer
     * @param truncated
     * @param recursionDesired
     * @param recursionAvailable
     * @param reserved
     * @param acceptNonAuthenticatedData
     * @param responseCode
     * @param question
     * @param answer
     * @param authority
     * @param additional
     */
    public DnsMessage(int transactionId, MessageType messageType, OpCode opCode, bool authoritativeAnswer,
                      bool truncated, bool recursionDesired, bool recursionAvailable, bool reserved,
                      bool acceptNonAuthenticatedData, ResponseCode responseCode, IList<QuestionRecord> question,
                      IList<IResourceRecord> answer, IList<IResourceRecord> authority,
                      IList<IResourceRecord> additional)
    {
        this.transactionId = transactionId;
        this.messageType = messageType;
        this.opCode = opCode;
        this.authoritativeAnswer = authoritativeAnswer;
        this.truncated = truncated;
        this.recursionDesired = recursionDesired;
        this.recursionAvailable = recursionAvailable;
        this.reserved = reserved;
        this.acceptNonAuthenticatedData = acceptNonAuthenticatedData;
        this.responseCode = responseCode;
        this.questionRecords = question;
        this.answerRecords = answer;
        this.authorityRecords = authority;
        this.additionalRecords = additional;
    }

    /**
     * @return Returns the acceptNonAuthenticatedData.
     */
    public bool IsAcceptNonAuthenticatedData()
    {
        return acceptNonAuthenticatedData;
    }

    /**
     * @return Returns the additional.
     */
    public IList<IResourceRecord> GetAdditionalRecords()
    {
        return additionalRecords;
    }

    /**
     * @return Returns the answers.
     */
    public IList<IResourceRecord> GetAnswerRecords()
    {
        return answerRecords;
    }

    /**
     * @return Returns the authoritativeAnswer.
     */
    public bool IsAuthoritativeAnswer()
    {
        return authoritativeAnswer;
    }

    /**
     * @return Returns the authority.
     */
    public IList<IResourceRecord> GetAuthorityRecords()
    {
        return authorityRecords;
    }

    /**
     * @return Returns the messageType.
     */
    public MessageType GetMessageType()
    {
        return messageType;
    }

    /**
     * @return Returns the opCode.
     */
    public OpCode GetOpCode()
    {
        return opCode;
    }

    /**
     * @return Returns the question.
     */
    public IList<QuestionRecord> GetQuestionRecords()
    {
        return questionRecords;
    }

    /**
     * @return Returns the recursionAvailable.
     */
    public bool IsRecursionAvailable()
    {
        return recursionAvailable;
    }

    /**
     * @return Returns the recursionDesired.
     */
    public bool IsRecursionDesired()
    {
        return recursionDesired;
    }

    /**
     * @return Returns the reserved.
     */
    public bool IsReserved()
    {
        return reserved;
    }

    /**
     * @return Returns the responseCode.
     */
    public ResponseCode GetResponseCode()
    {
        return responseCode;
    }

    /**
     * @return Returns the transactionId.
     */
    public int GetTransactionId()
    {
        return transactionId;
    }

    /**
     * @return Returns the truncated.
     */
    public bool IsTruncated()
    {
        return truncated;
    }

    public override string ToString()
    {
        return new StringBuilder("DnsMessage[")
            .Append("transactionId=").Append(transactionId.ToString(CultureInfo.InvariantCulture))
            .Append(",opCode=").Append(opCode)
            .Append(",truncated=").Append(truncated ? "true" : "false")
            .Append(",messageType=").Append(messageType)
            .Append(",recursionDesired=").Append(recursionDesired ? "true" : "false")
            .Append(",additionalRecords=").Append(ListToString(additionalRecords))
            .Append(",responseCode=").Append(responseCode)
            .Append(",authorityRecords=").Append(ListToString(authorityRecords))
            .Append(",acceptNonAuthenticatedData=").Append(acceptNonAuthenticatedData ? "true" : "false")
            .Append(",recursionAvailable=").Append(recursionAvailable ? "true" : "false")
            .Append(",answerRecords=").Append(ListToString(answerRecords))
            .Append(",questionRecords=").Append(ListToString(questionRecords))
            .Append(",authoritativeAnswer=").Append(authoritativeAnswer ? "true" : "false")
            .Append(",reserved=").Append(reserved ? "true" : "false")
            .Append(']').ToString();
    }

    // CLR: java.util.AbstractCollection.toString ([a, b]); null prints "<null>" as ToStringBuilder does.
    private static string ListToString<T>(IList<T> list)
    {
        if (list == null)
        {
            return "<null>";
        }

        StringBuilder sb = new StringBuilder("[");

        for (int i = 0; i < list.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            sb.Append(list[i]);
        }

        return sb.Append(']').ToString();
    }
}
