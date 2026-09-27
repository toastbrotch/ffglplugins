#pragma once
#include <string>
#include <vector>
#include <map>

// A small, self-contained JSON reader (copied from VideoPong's VideoPongJson, renamed) — just
// enough to pull the DESCRIPTION/INPUTS fields out of an ISF shader's /*{ ... }*/ header without
// pulling in a large third-party dependency for it.
namespace isfjson
{
class JsonValue
{
public:
	enum class Type
	{
		Null,
		Boolean,
		Number,
		String,
		Array,
		Object
	};

	JsonValue();

	Type GetType() const { return type; }
	bool IsNull() const { return type == Type::Null; }
	bool IsObject() const { return type == Type::Object; }
	bool IsArray() const { return type == Type::Array; }

	//Object member access. Returns a Null value if this isn't an object or the key isn't present.
	const JsonValue& operator[]( const std::string& key ) const;
	bool HasMember( const std::string& key ) const;

	//Array element access. Returns a Null value if this isn't an array or the index is out of range.
	const JsonValue& operator[]( size_t index ) const;
	size_t Size() const;

	std::string AsString( const std::string& fallback = std::string() ) const;
	double AsDouble( double fallback = 0.0 ) const;
	int AsInt( int fallback = 0 ) const;
	bool AsBool( bool fallback = false ) const;

	//Parses `text` into `out`. Returns false (and fills `errorOut`, if given) on malformed input.
	static bool Parse( const std::string& text, JsonValue& out, std::string* errorOut = nullptr );

private:
	friend class JsonParser;

	Type type;
	bool boolValue;
	double numberValue;
	std::string stringValue;
	std::vector< JsonValue > arrayValue;
	std::map< std::string, JsonValue > objectValue;
};

}//namespace isfjson
