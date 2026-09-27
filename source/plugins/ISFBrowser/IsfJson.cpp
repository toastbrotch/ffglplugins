#include "IsfJson.h"
#include <cctype>
#include <cstdlib>
#include <cstring>

namespace isfjson
{
JsonValue::JsonValue() :
	type( Type::Null ),
	boolValue( false ),
	numberValue( 0.0 )
{
}

const JsonValue& JsonValue::operator[]( const std::string& key ) const
{
	static const JsonValue nullValue;
	if( type != Type::Object )
		return nullValue;
	auto it = objectValue.find( key );
	if( it == objectValue.end() )
		return nullValue;
	return it->second;
}

bool JsonValue::HasMember( const std::string& key ) const
{
	return type == Type::Object && objectValue.find( key ) != objectValue.end();
}

const JsonValue& JsonValue::operator[]( size_t index ) const
{
	static const JsonValue nullValue;
	if( type != Type::Array || index >= arrayValue.size() )
		return nullValue;
	return arrayValue[ index ];
}

size_t JsonValue::Size() const
{
	if( type == Type::Array )
		return arrayValue.size();
	if( type == Type::Object )
		return objectValue.size();
	return 0;
}

std::string JsonValue::AsString( const std::string& fallback ) const
{
	return type == Type::String ? stringValue : fallback;
}
double JsonValue::AsDouble( double fallback ) const
{
	return type == Type::Number ? numberValue : fallback;
}
int JsonValue::AsInt( int fallback ) const
{
	return type == Type::Number ? (int)numberValue : fallback;
}
bool JsonValue::AsBool( bool fallback ) const
{
	return type == Type::Boolean ? boolValue : fallback;
}

	//A plain recursive-descent JSON parser. Not declared in the header, JsonValue::Parse below is
	//the only entry point external code needs.
	class JsonParser
	{
	public:
		explicit JsonParser( const std::string& text ) :
			text( text ), pos( 0 )
		{
		}

		bool Parse( JsonValue& out )
		{
			SkipWhitespace();
			if( !ParseValue( out ) )
				return false;
			SkipWhitespace();
			//Trailing garbage after the root value is treated as an error.
			if( pos < text.size() )
			{
				error = "Unexpected trailing data";
				return false;
			}
			return true;
		}

		std::string error;

	private:
		const std::string& text;
		size_t pos;

		char Peek() const { return pos < text.size() ? text[ pos ] : '\0'; }
		char Get() { return pos < text.size() ? text[ pos++ ] : '\0'; }
		bool Eof() const { return pos >= text.size(); }

		void SkipWhitespace()
		{
			while( !Eof() && ( Peek() == ' ' || Peek() == '\t' || Peek() == '\n' || Peek() == '\r' ) )
				++pos;
		}

		bool Expect( char c )
		{
			if( Peek() != c )
			{
				error = std::string( "Expected '" ) + c + "'";
				return false;
			}
			++pos;
			return true;
		}

		bool Consume( const char* literal )
		{
			size_t len = strlen( literal );
			if( text.compare( pos, len, literal ) != 0 )
				return false;
			pos += len;
			return true;
		}

		bool ParseValue( JsonValue& out )
		{
			SkipWhitespace();
			if( Eof() )
			{
				error = "Unexpected end of input";
				return false;
			}

			char c = Peek();
			if( c == '{' )
				return ParseObject( out );
			if( c == '[' )
				return ParseArray( out );
			if( c == '"' )
				return ParseString( out );
			if( c == 't' || c == 'f' )
				return ParseBool( out );
			if( c == 'n' )
				return ParseNull( out );
			if( c == '-' || ( c >= '0' && c <= '9' ) )
				return ParseNumber( out );

			error = "Unexpected character";
			return false;
		}

		bool ParseObject( JsonValue& out )
		{
			if( !Expect( '{' ) )
				return false;
			out       = JsonValue();
			out.type  = JsonValue::Type::Object;

			SkipWhitespace();
			if( Peek() == '}' )
			{
				++pos;
				return true;
			}

			while( true )
			{
				SkipWhitespace();
				if( Peek() != '"' )
				{
					error = "Expected object key string";
					return false;
				}
				JsonValue keyValue;
				if( !ParseString( keyValue ) )
					return false;

				SkipWhitespace();
				if( !Expect( ':' ) )
					return false;

				JsonValue memberValue;
				if( !ParseValue( memberValue ) )
					return false;

				out.objectValue[ keyValue.AsString() ] = std::move( memberValue );

				SkipWhitespace();
				if( Peek() == ',' )
				{
					++pos;
					continue;
				}
				break;
			}

			SkipWhitespace();
			return Expect( '}' );
		}

		bool ParseArray( JsonValue& out )
		{
			if( !Expect( '[' ) )
				return false;
			out      = JsonValue();
			out.type = JsonValue::Type::Array;

			SkipWhitespace();
			if( Peek() == ']' )
			{
				++pos;
				return true;
			}

			while( true )
			{
				JsonValue elementValue;
				if( !ParseValue( elementValue ) )
					return false;
				out.arrayValue.push_back( std::move( elementValue ) );

				SkipWhitespace();
				if( Peek() == ',' )
				{
					++pos;
					continue;
				}
				break;
			}

			SkipWhitespace();
			return Expect( ']' );
		}

		bool ParseString( JsonValue& out )
		{
			if( !Expect( '"' ) )
				return false;

			std::string result;
			while( true )
			{
				if( Eof() )
				{
					error = "Unterminated string";
					return false;
				}
				char c = Get();
				if( c == '"' )
					break;

				if( c != '\\' )
				{
					result += c;
					continue;
				}

				if( Eof() )
				{
					error = "Unterminated escape sequence";
					return false;
				}
				char escaped = Get();
				switch( escaped )
				{
				case '"':
					result += '"';
					break;
				case '\\':
					result += '\\';
					break;
				case '/':
					result += '/';
					break;
				case 'b':
					result += '\b';
					break;
				case 'f':
					result += '\f';
					break;
				case 'n':
					result += '\n';
					break;
				case 'r':
					result += '\r';
					break;
				case 't':
					result += '\t';
					break;
				case 'u':
				{
					if( pos + 4 > text.size() )
					{
						error = "Invalid unicode escape";
						return false;
					}
					unsigned int codepoint = 0;
					for( int i = 0; i < 4; ++i )
					{
						char hexDigit = Get();
						codepoint <<= 4;
						if( hexDigit >= '0' && hexDigit <= '9' )
							codepoint |= (unsigned int)( hexDigit - '0' );
						else if( hexDigit >= 'a' && hexDigit <= 'f' )
							codepoint |= (unsigned int)( hexDigit - 'a' + 10 );
						else if( hexDigit >= 'A' && hexDigit <= 'F' )
							codepoint |= (unsigned int)( hexDigit - 'A' + 10 );
						else
						{
							error = "Invalid unicode escape";
							return false;
						}
					}
					AppendUtf8( result, codepoint );
					break;
				}
				default:
					error = "Invalid escape sequence";
					return false;
				}
			}

			out             = JsonValue();
			out.type        = JsonValue::Type::String;
			out.stringValue = std::move( result );
			return true;
		}

		static void AppendUtf8( std::string& out, unsigned int codepoint )
		{
			//ISF headers aren't expected to contain characters outside the basic multilingual
			//plane, so surrogate pairs aren't handled here.
			if( codepoint <= 0x7F )
			{
				out += (char)codepoint;
			}
			else if( codepoint <= 0x7FF )
			{
				out += (char)( 0xC0 | ( codepoint >> 6 ) );
				out += (char)( 0x80 | ( codepoint & 0x3F ) );
			}
			else
			{
				out += (char)( 0xE0 | ( codepoint >> 12 ) );
				out += (char)( 0x80 | ( ( codepoint >> 6 ) & 0x3F ) );
				out += (char)( 0x80 | ( codepoint & 0x3F ) );
			}
		}

		bool ParseBool( JsonValue& out )
		{
			if( Consume( "true" ) )
			{
				out           = JsonValue();
				out.type      = JsonValue::Type::Boolean;
				out.boolValue = true;
				return true;
			}
			if( Consume( "false" ) )
			{
				out           = JsonValue();
				out.type      = JsonValue::Type::Boolean;
				out.boolValue = false;
				return true;
			}
			error = "Invalid literal";
			return false;
		}

		bool ParseNull( JsonValue& out )
		{
			if( Consume( "null" ) )
			{
				out = JsonValue();
				return true;
			}
			error = "Invalid literal";
			return false;
		}

		bool ParseNumber( JsonValue& out )
		{
			size_t start = pos;
			if( Peek() == '-' )
				++pos;
			while( !Eof() && isdigit( (unsigned char)Peek() ) )
				++pos;
			if( Peek() == '.' )
			{
				++pos;
				while( !Eof() && isdigit( (unsigned char)Peek() ) )
					++pos;
			}
			if( Peek() == 'e' || Peek() == 'E' )
			{
				++pos;
				if( Peek() == '+' || Peek() == '-' )
					++pos;
				while( !Eof() && isdigit( (unsigned char)Peek() ) )
					++pos;
			}

			if( pos == start )
			{
				error = "Invalid number";
				return false;
			}

			out             = JsonValue();
			out.type        = JsonValue::Type::Number;
			out.numberValue = strtod( text.c_str() + start, nullptr );
			return true;
		}
	};

bool JsonValue::Parse( const std::string& text, JsonValue& out, std::string* errorOut )
{
	JsonParser parser( text );
	if( !parser.Parse( out ) )
	{
		if( errorOut )
			*errorOut = parser.error;
		return false;
	}
	return true;
}

}//namespace isfjson
