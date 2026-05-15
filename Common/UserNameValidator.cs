using System;
using System.Linq;

namespace Common;

public static class UserNameValidator {

	#region Constants

	public const int MinLength = 1;
	public const int MaxLength = 16;

	#endregion

	#region Public API

	public static bool IsValid(string? userName) {
		if (userName is null) return false;
		switch (userName.Length) {
			case < MinLength:
			case > MaxLength:
				return false;
		}

		if (userName.Any(char.IsWhiteSpace)) return false;

		return true;
	}

	public static void Validate(string? userName) {
		if (!IsValid(userName)) {
			throw new ArgumentException($"Имя должно быть от {MinLength} до {MaxLength} символов и не содержать пробелы.");
		}
	}

	#endregion
}
