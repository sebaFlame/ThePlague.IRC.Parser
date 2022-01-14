using System;
using System.Buffers;

using ThePlague.IRC.Parser.Tokens;

namespace ThePlague.IRC.Parser
{
    public static partial class IRCParser
    {
        //parse an integer
        private static bool TryParseInteger
        (
            ref SequenceReader<byte> reader,
            out Token integer
        )
        {
            SequencePosition startPosition = reader.Position;
            bool found = false;

            while(MatchDigit(ref reader))
            {
                found = true;
            }

            if(!found)
            {
                integer = null;
                return false;
            }

            integer = new Token
            (
                TokenType.Integer,
                reader.Sequence.Slice(startPosition, reader.Position)
            );

            return true;
        }

        //parse a nickname
        public static Token ParseNickname
        (
            ref SequenceReader<byte> reader
        )
        {
            SequencePosition startPosition = reader.Position;
            byte value;
            Token first;

            //parse first letter of the nickname
            if(IsLetter(ref reader, out value)
                || IsSpecial(value))
            {
                reader.Advance(1);

                //value should be 1-1 translation to tokentype
                //create a token for the terminal
                first = new Token
                (
                    (TokenType)value,
                    reader.Sequence.Slice(startPosition, reader.Position)
                );

                //parse rest of nickname
                first.Combine
                (
                    ParseNicknameSuffix(ref reader)
                );

                return new Token
                (
                    TokenType.Nickname,
                    reader.Sequence.Slice(startPosition, reader.Position),
                    first
                );
            }

            throw new ParserException("Incorrect first terminal for nickname");
        }

        //parse rest of the nickname
        private static Token ParseNicknameSuffix
        (
            ref SequenceReader<byte> reader
        )
        {
            SequencePosition startPosition = reader.Position;
            byte value;
            bool found = false;

            while(IsAlphaNumeric(ref reader, out value)
                  || IsSpecial(value)
                  || IsTerminal(TokenType.Minus, value))
            {
                reader.Advance(1);
                found = true;
            }

            //can be empty
            if(!found)
            {
                return new Token
                (
                    TokenType.NicknameSuffix
                );
            }

            return new Token
            (
                TokenType.NicknameSuffix,
                reader.Sequence.Slice(startPosition, reader.Position)
            );
        }

        //parse a username
        public static Token ParseUsername
        (
            ref SequenceReader<byte> reader
        )
        {
            SequencePosition startPosition = reader.Position;

            while(IsUTF8WithoutNullCrLfSpaceAt(ref reader, out _))
            {
                reader.Advance(1);
            }

            return new Token
            (
                TokenType.Username,
                reader.Sequence.Slice(startPosition, reader.Position)
            );
        }

        public static Token ParseHost
        (
            ref SequenceReader<byte> reader
        )
        {
            SequencePosition startPosition = reader.Position;

            Token shortName = ParseShortName(ref reader);

            Token hostSuffix = ParseHostSuffix(ref reader);

            shortName.Combine(hostSuffix);

            return new Token
            (
                TokenType.Host,
                reader.Sequence.Slice(startPosition, reader.Position),
                shortName
            );
        }

        //parse a short name
        private static Token ParseShortName
        (
            ref SequenceReader<byte> reader
        )
        {
            SequencePosition startPosition = reader.Position;

            //parse the shortname prefix
            Token shortNamePrefix = ParseShortNamePrefix(ref reader);

            //parse the shortname suffix
            Token shortNameSuffix = ParseShortNameSuffix(ref reader);

            //link prefix and suffix together
            shortNamePrefix.Combine(shortNameSuffix);

            return new Token
            (
                TokenType.ShortName,
                reader.Sequence.Slice(startPosition, reader.Position),
                shortNamePrefix
            );
        }

        private static Token ParseShortNamePrefix
        (
            ref SequenceReader<byte> reader
        )
        {
            SequencePosition startPosition = reader.Position;

            //a short name start with an alphanumeric
            if(MatchAlphaNumeric(ref reader))
            {
                return new Token
                (
                    TokenType.ShortNamePrefix,
                    reader.Sequence.Slice(startPosition, reader.Position)
                );
            }
            else
            {
                throw new ParserException("Alphanumeric expected");
            }
        }

        //parse a shortname suffix, or return empty
        private static Token ParseShortNameSuffix
        (
            ref SequenceReader<byte> reader
        )
        {
            SequencePosition startPosition = reader.Position;

            //can return an empty token
            if(!TryParseShortNameList(ref reader, out Token shortNameList))
            {
                return new Token
                (
                    TokenType.ShortNameSuffix
                );
            }
            //or return a list of shortname terminals
            else
            {
                return new Token
                (
                    TokenType.ShortNameSuffix,
                    reader.Sequence.Slice(startPosition, reader.Position),
                    shortNameList
                );
            }
        }

        //parse a list of terminals as shortname
        private static bool TryParseShortNameList
        (
            ref SequenceReader<byte> reader,
            out Token shortNameList
        )
        {
            SequencePosition startPosition = reader.Position;
            byte value;
            bool found = false;

            //Match 0-9, a-z, A-Z or hyphen and advance if found
            while(IsAlphaNumeric(ref reader, out value)
               || IsTerminal(TokenType.Minus, value))
            {
                found = true;
                reader.Advance(1);
            }

            if(!found)
            {
                shortNameList = null;
                return false;
            }

            shortNameList = new Token
            (
                TokenType.ShortNameSuffix,
                reader.Sequence.Slice(startPosition, reader.Position)
            );

            return true;
        }

        private static Token ParseHostSuffix
        (
            ref SequenceReader<byte> reader
        )
        {
            SequencePosition startPosition = reader.Position;
            Token previous = null, first = null, period;
            bool found = false;

            //multiple tags are seperated by a semicolon
            while(TryParseTerminal
            (
                TokenType.Period,
                ref reader,
                out period
            ))
            {
                //add semicolon to linked list
                previous = previous.Combine(period);

                //parse tag and add to children
                previous = previous.Combine
                (
                    ParseShortName(ref reader)
                );

                if(first is null)
                {
                    first = previous;
                }

                found = true;
            }

            //can return an empty lsit
            if(!found)
            {
                return new Token
                (
                    TokenType.HostSuffix
                );
            }

            return new Token
            (
                TokenType.HostSuffix,
                reader.Sequence.Slice(startPosition, reader.Position),
                first
            );
        }

        public static Token ParseUserHost
        (
            ref SequenceReader<byte> reader
        )
        {
            SequencePosition startPosition = reader.Position;

            Token nickname = ParseNickname(ref reader);

            if(!TryParseUserHostUsername
            (
                ref reader,
                out Token userName
            ))
            {
                throw new ParserException("Username expected");
            }

            if(!TryParseUserHostHostname
            (
                ref reader,
                out Token hostname
            ))
            {
                throw new ParserException("Hostname expected");
            }

            nickname
                .Combine(userName)
                .Combine(hostname);

            return new Token
            (
                TokenType.UserHost,
                reader.Sequence.Slice(startPosition, reader.Position),
                nickname
            );
        }

        private static bool TryParseUserHostUsername
        (
            ref SequenceReader<byte> reader,
            out Token userName
        )
        {
            SequencePosition startPosition = reader.Position;

            if(!TryParseTerminal
            (
                TokenType.ExclamationMark,
                ref reader,
                out Token exclamation
            ))
            {
                userName = null;
                return false;
            }

            exclamation.Combine(ParseUsername(ref reader));

            userName = new Token
            (
                TokenType.UserHostUsername,
                reader.Sequence.Slice(startPosition, reader.Position),
                exclamation
            );

            return true;
        }

        private static bool TryParseUserHostHostname
        (
            ref SequenceReader<byte> reader,
            out Token hostName
        )
        {
            SequencePosition startPosition = reader.Position;

            if(!TryParseTerminal
            (
                TokenType.AtSign,
                ref reader,
                out Token at
            ))
            {
                hostName = null;
                return false;
            }

            at.Combine(ParseHost(ref reader));

            hostName = new Token
            (
                TokenType.UserHostHostname,
                reader.Sequence.Slice(startPosition, reader.Position),
                at
            );

            return true;
        }

        //parse a channel
        public static Token ParseChannel
        (
            ref SequenceReader<byte> reader
        )
        {
            SequencePosition startPosition = reader.Position;

            if(!TryParseChannelPrefix(ref reader, out Token channelPrefix))
            {
                throw new ParserException("Channel prefix expected");
            }

            channelPrefix
                .Combine(ParseChannelString(ref reader))
                .Combine(ParseChannelSuffix(ref reader));

            return new Token
            (
                TokenType.Channel,
                reader.Sequence.Slice(startPosition, reader.Position),
                channelPrefix
            );
        }

        //parse channel prefixes (including channel membership prefixes)
        private static bool TryParseChannelPrefix
        (
            ref SequenceReader<byte> reader,
            out Token channelPrefix
        )
        {
            SequencePosition startPosition = reader.Position;
            Token prefix;

            //extra (non-default channel prefixes)
            if(TryParseTerminal(TokenType.Ampersand, ref reader, out prefix)
                || TryParseTerminal(TokenType.Plus, ref reader, out prefix)
                || TryParseChannelPrefixWithoutMembership
                    (
                        ref reader,
                        out prefix
                    ))
            {
                channelPrefix = new Token
                (
                    TokenType.ChannelPrefix,
                    reader.Sequence.Slice(startPosition, reader.Position),
                    prefix
                );

                return true;
            }

            channelPrefix = null;
            return false;
        }

        //parse channel prefixes excluding channel membership prefixes
        private static bool TryParseChannelPrefixWithoutMembership
        (
            ref SequenceReader<byte> reader,
            out Token channelPrefix
        )
        {
            SequencePosition startPosition = reader.Position;
            Token prefix;

            //default channel prefix
            if(TryParseTerminal(TokenType.Number, ref reader, out prefix))
            {
                channelPrefix = new Token
                (
                    TokenType.ChannelPrefixWithoutMembership,
                    reader.Sequence.Slice(startPosition, reader.Position),
                    prefix
                );

                return true;
            }

            //if an exclamationmark is found, a channelid is expected
            if(TryParseTerminal
            (
                TokenType.ExclamationMark,
                ref reader,
                out prefix
            ))
            {
                if(TryParseChannelId
                (
                    ref reader,
                    out Token channelId
                ))
                {
                    prefix.Combine(channelId);

                    channelPrefix = new Token
                    (
                        TokenType.ChannelPrefixWithoutMembership,
                        reader.Sequence.Slice(startPosition, reader.Position),
                        prefix
                    );

                    return true;

                }
                else
                {
                    throw new ParserException("ChannelId expected");
                }
            }

            channelPrefix = null;
            return false;
        }

        private static bool TryParseChannelId
        (
            ref SequenceReader<byte> reader,
            out Token channelId
        )
        {
            SequencePosition startPosition = reader.Position;
            int i = 1;

            //ChannelId consists of 5 digits or uppercase
            for(; i <= 5; i++)
            {
                if(IsUpperCaseOrDigit(ref reader, out _))
                {
                    reader.Advance(1);
                }
                else
                {
                    break;
                }
            }

            if(i != 6)
            {
                channelId = null;
                return false;
            }

            channelId = new Token
            (
                TokenType.ChannelId,
                reader.Sequence.Slice(startPosition, reader.Position)
            );

            return true;
        }

        private static Token ParseChannelString
        (
            ref SequenceReader<byte> reader
        )
        {
            SequencePosition startPosition = reader.Position;

            while(IsUTF8WithoutNullBellCrLfSpaceCommaAndColon
            (
                ref reader,
                out _
            ))
            {
                reader.Advance(1);
            }

            return new Token
            (
                TokenType.ChannelString,
                reader.Sequence.Slice(startPosition, reader.Position)
            );
        }

        //parse a channel suffix or return empty
        private static Token ParseChannelSuffix
        (
            ref SequenceReader<byte> reader
        )
        {
            SequencePosition startPosition = reader.Position;

            //can be empty
            if(!TryParseTerminal(TokenType.Colon, ref reader, out Token colon))
            {
                return new Token
                (
                    TokenType.ChannelSuffix
                );
            }

            Token channelString = ParseChannelString(ref reader);

            colon.Combine(channelString);

            return new Token
            (
                TokenType.ChannelSuffix,
                reader.Sequence.Slice(startPosition, reader.Position),
                colon
            );
        }
    }
}