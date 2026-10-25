using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace AchievementDrift.Core;

// Único sitio que toca TimeZoneInfo. Las builds recortadas de ASF (win-x64, linux-x64…) no exponen ese tipo al plugin
// (System.Runtime perdió su reenvío) ni FindSystemTimeZoneById: ahí se usa la hora local del sistema y no se admite
// TimeZone. Los métodos que nombran TimeZoneInfo van aparte y sin inlining para que el JIT no los resuelva si no se llaman.
internal static class TimeZones {
	// Lo pone a false el plugin al cargar si la build no tiene TimeZoneInfo
	internal static bool Available { get; set; } = true;

	internal static string LocalId => Available ? GetLocalId() : "local";

	// Convierte de UTC a la hora local de la zona indicada (o la del sistema si no hay zona válida)
	internal static Func<DateTime, DateTime> ToLocal(string? id) => Available ? CreateConverter(id) : static utc => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();

	internal static bool IsValid(string? id) => Available && Exists(id);

	internal static bool TryFind(string? id, [NotNullWhen(true)] out TimeZoneInfo? timeZone) {
		timeZone = null;

		if (string.IsNullOrWhiteSpace(id)) {
			return false;
		}

		try {
			timeZone = TimeZoneInfo.FindSystemTimeZoneById(id.Trim());

			return true;
		} catch (TimeZoneNotFoundException) {
			return false;
		} catch (InvalidTimeZoneException) {
			return false;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool Exists(string? id) => TryFind(id, out _);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string GetLocalId() => TimeZoneInfo.Local.Id;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static Func<DateTime, DateTime> CreateConverter(string? id) {
		TimeZoneInfo timeZone = TryFind(id, out TimeZoneInfo? found) ? found : TimeZoneInfo.Local;

		return utc => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), timeZone);
	}
}
