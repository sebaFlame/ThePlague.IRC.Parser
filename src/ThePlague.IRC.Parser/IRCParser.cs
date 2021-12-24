using System;
using System.Buffers;
using System.Runtime.CompilerServices;

using ThePlague.IRC.Parser.Tokens;

namespace ThePlague.IRC.Parser
{
    public static partial class IRCParser
    {
        public static bool TryParse
        (
            in ReadOnlySequence<byte> sequence,
            out Token token
        )
        {
            //sequence is empty
            if(sequence.IsEmpty)
            {
                token = null;
                return false;
            }

            //check if sequence contains a LineFeed (a full message)
            SequencePosition? lf = sequence.PositionOf
            (
                (byte)TokenType.LineFeed
            );

            if(!lf.HasValue)
            {
                token = null;
                return false;
            }

            ReadOnlySequence<byte> message = sequence.Slice
            (
                0,
                sequence.GetOffset(lf.Value) + 1 //slice WITH LineFeed included
            );

            SequenceReader<byte> sequenceReader
                = new SequenceReader<byte>(message);

            token = ParseMessage(ref sequenceReader);
            return true;
        }

        //combine 2 tokens as linked list and return currently added item
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Token Combine(Token left, Token right)
        {
            if(left is null)
            {
                return right;
            }

            //TODO: verify
            if(right is null)
            {
                return left;
            }

            Token leftToken = left.GetLastToken();

            leftToken.Next = right;
            right.Previous = leftToken;

            return right;
        }
    }
}