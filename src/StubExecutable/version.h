/*
The MIT License (MIT)

Copyright (c) 2015 Marko Zivanovic

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

#pragma once

#include <ostream>
#include <string>
#include <vector>

namespace version {

	/// Any error in parsing or validation of version string will result in Parse_error exception being thrown.
	class Parse_error : public std::runtime_error {
		using std::runtime_error::runtime_error;
	};

	/// Type of prerelease identifier: alphanumeric or numeric.
	/**
	Type of identifier affects comparison: alphanumeric identifiers are compared as ASCII strings, while
	numeric identifiers are compared as numbers.
	*/
	enum class Id_type {
		alnum, ///< Identifier is alphanumerical
		num ///< Identifier is numeric
	};

	/// Container for prerelease identifier value and it's type.
	/**
	Prerelease version string consist of an optional series of dot-separated identifiers.
	These identifiers can be either numerical or alphanumerical.
	This structure describes one such identifier.
	*/
	using Prerelease_identifier = std::pair<std::string, Id_type>;

	/// Container for all prerelease identifiers for a given version string.
	using Prerelease_identifiers = std::vector<Prerelease_identifier>;

	/// Build identifier is arbitrary string with no special meaning with regards to version precedence.
	using Build_identifier = std::string;

	/// Container for all build identifiers of a given version string.
	using Build_identifiers = std::vector<Build_identifier>;

	/// Description of version broken into parts, as per semantic versioning specification.
	struct Version_data {
		int major; ///< Major version, change only on incompatible API modifications.
		int minor; ///< Minor version, change on backwards-compatible API modifications.
		int patch; ///< Patch version, change only on bugfixes.

		/// Optional series of prerelease identifiers.
		Prerelease_identifiers prerelease_ids;

		/// Optional series of build identifiers.
		Build_identifiers build_ids;
	};

	// Forward declaration required for operators' template declarations.
	template<typename Parser, typename Comparator>
	class Basic_version;

	/// Test if left-hand version operand is of lower precedence than the right-hand version.
	template<typename Parser, typename Comparator>
	bool operator<(const Basic_version<Parser, Comparator>&,
		const Basic_version<Parser, Comparator>&);

	/// Test if left-hand version operand if of equal precedence as the right-hand version.
	template<typename Parser, typename Comparator>
	bool operator==(const Basic_version<Parser, Comparator>&,
		const Basic_version<Parser, Comparator>&);

	/// Output version object to stream using standard semver format (X.Y.Z-PR+B).
	template<typename Parser, typename Comparator>
	std::ostream& operator<<(std::ostream&,
		const Basic_version<Parser, Comparator>&);

	/// Test if left-hand version and right-hand version are of different precedence.
	template<typename Parser, typename Comparator>
	bool operator!=(const Basic_version<Parser, Comparator>&,
		const Basic_version<Parser, Comparator>&);

	/// Test if left-hand version operand is of higher precedence than the right-hand version.
	template<typename Parser, typename Comparator>
	bool operator>(const Basic_version<Parser, Comparator>&,
		const Basic_version<Parser, Comparator>&);

	/// Test if left-hand version operand is of higher or equal precedence as the right-hand version.
	template<typename Parser, typename Comparator>
	bool operator>=(const Basic_version<Parser, Comparator>&,
		const Basic_version<Parser, Comparator>&);

	/// Test if left-hand version operand is of lower or equal precedence as the right-hand version.
	template<typename Parser, typename Comparator>
	bool operator<=(const Basic_version<Parser, Comparator>&,
		const Basic_version<Parser, Comparator>&);


	/// Base class for various version parsing and precedence ordering schemes.
	/**
	Basic_version class describes general version object without prescribing parsing,
	validation and comparison rules. These rules are implemented by supplied Parser and
	Comparator objects.
	*/
	template<typename Parser, typename Comparator>
	class Basic_version {
	public:
		/// Construct Basic_version object using Parser object to parse default ("0.0.0") version string and Comparator for comparison.
		Basic_version(Parser, Comparator);

		/// Construct Basic_version object using Parser to parse supplied version string and Comparator for comparison.
		Basic_version(const std::string&, Parser, Comparator);

		/// Construct Basic_version by copying data from another one.
		Basic_version(const Basic_version&);

		/// Copy version data from another Basic_version to this one.
		Basic_version& operator=(const Basic_version&);

		int major() const; ///< Get major version.
		int minor() const; ///< Get minor version.
		int patch() const; ///< Get patch version.
		const std::string prerelease() const; ///< Get prerelease version string.
		const std::string build() const; ///< Get build version string.

		friend bool operator< <>(const Basic_version&, const Basic_version&);
		friend bool operator== <>(const Basic_version&, const Basic_version&);
		friend std::ostream& operator<< <>(std::ostream&s, const Basic_version&);

	private:
		Parser parser_;
		Comparator comparator_;
		Version_data ver_;
	};
}

#include "version.inl"
