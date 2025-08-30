using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Contracts;

namespace Netty.NET.Common.Internal;

public struct CharSequenceEnumerator : IEnumerator<char>
{
    private ICharSequence _charSequence;
    private int _index;
    private char _currentElement;

    internal CharSequenceEnumerator(ICharSequence charSequence)
    {
        Contract.Requires(charSequence != null);

        _charSequence = charSequence;
        _index = -1;
        _currentElement = (char)0;
    }

    public bool MoveNext()
    {
        if (_index < _charSequence.Count - 1)
        {
            _index++;
            _currentElement = _charSequence[_index];
            return true;
        }

        _index = _charSequence.Count;
        return false;
    }

    object IEnumerator.Current
    {
        get
        {
            if (_index == -1)
            {
                throw new InvalidOperationException("Enumerator not initialized.");
            }

            if (_index >= _charSequence.Count)
            {
                throw new InvalidOperationException("Eumerator already completed.");
            }

            return _currentElement;
        }
    }

    public char Current
    {
        get
        {
            if (_index == -1)
            {
                throw new InvalidOperationException("Enumerator not initialized.");
            }

            if (_index >= _charSequence.Count)
            {
                throw new InvalidOperationException("Eumerator already completed.");
            }

            return _currentElement;
        }
    }

    public void Reset()
    {
        _index = -1;
        _currentElement = (char)0;
    }

    public void Dispose()
    {
        if (_charSequence != null)
        {
            _index = _charSequence.Count;
        }

        _charSequence = null;
    }
}