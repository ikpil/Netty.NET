using System.Collections.Generic;

namespace Netty.NET.Common.Internal;

public interface ICharSequence : IReadOnlyList<char>
{
    /// Start is the inclusive start index to begin the subsequence.
    /// End is the exclusive end index to end the subsequence.
    ICharSequence subSequence(int start, int end);

    ICharSequence subSequence(int start);

    char charAt(int index);
    int length();
    int indexOf(char ch, int start = 0);

    bool regionMatches(int thisStart, ICharSequence seq, int start, int length);

    bool regionMatchesIgnoreCase(int thisStart, ICharSequence seq, int start, int length);

    bool contentEquals(ICharSequence other);

    bool contentEqualsIgnoreCase(ICharSequence other);

    int hashCode(bool ignoreCase);

    string ToString(int start);
}