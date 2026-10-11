# CrowRx.Core

The foundational core library for the CrowRx ecosystem, providing high-performance utilities, extension methods, and a flexible type conversion system.

## API Reference

### 1. Logging System (`Log` Static Class)
A conditional logging system that supports custom logger injection and build-specific log filtering.

*   **`Log.SetLogger(ILogger? logger)`**: Injects a custom implementation of `ILogger`.
*   **`Log.Info`, `Warning`, `Error`, `Assertion`, `Exception`**: Logs messages based on defined compilation symbols.

**Example:**
```csharp
// Set a custom logger (e.g., Unity Debug)
Log.SetLogger(new UnityLogger()); 

Log.Info("System initialized.");
Log.Error("Failed to load data.");
```

**Compilation Symbols:**
`CROWRX_LOG_INFO`, `CROWRX_LOG_WARNING`, `CROWRX_LOG_ERROR`, `CROWRX_LOG_ASSERT`, `CROWRX_LOG_EXCEPTION`, `CROWRX_LOG_ALL`, `UNITY_EDITOR`

---

### 2. Enum Extensions (`EnumValueExtension` and `EnumAttributeExtension` Static Classes)
Utilities for extracting the exact underlying enum value, comparing enum values, and caching member attributes.

*   **`ToValue<TEnum, TValue>()`**: Returns the underlying value without boxing. `TValue` must match the enum's actual underlying type; a mismatch throws an exception.
*   **`TryToValue<TEnum, TValue>(out value)`**: Returns whether the underlying type matches and supplies the value on success.
*   **`EqualsValue(enum2)`**: Compares enum numeric values while preserving signedness and the full integer range.
*   **`GetAttributeOfType<TEnum, TAttribute>()`**: Retrieves the first matching member attribute without boxing the enum argument. Results are cached and attribute instances are shared between callers. Missing attributes and unnamed flag combinations return null.

`ToInt` and `EqualsEnum` are obsolete. The former one-type-argument `GetAttributeOfType<T>()` overload has been removed; specify both the enum and attribute types.

**Example:**
```csharp
using System.ComponentModel;

enum Status
{
    [Description("Active")]
    Active = 1,
    Inactive = 0
}

int value = Status.Active.ToValue<Status, int>(); // 1
bool converted = Status.Active.TryToValue<Status, int>(out int rawValue);
bool isActive = Status.Active.EqualsValue(Status.Active);
var desc = Status.Active.GetAttributeOfType<Status, DescriptionAttribute>();
```

---

### 3. Object & String Extensions (`ObjectExtension` Static Class)
Robust type conversion and string parsing utilities.

*   **`ConvertTo<T>()`**: Converts an object to type `T` (supports `Nullable` and `InvariantCulture`).
*   **`ParseTo<T>()`**: Parses a string into type `T` (supports primitives, `Enum`, and `Nullable`).
*   **`TryConvertTo<T>(out result)`**, **`TryParseTo<T>(out result)`**: Return success separately from the result, distinguishing a valid zero from a failed conversion. Expected failures return false without logging.

All conversions use `InvariantCulture`. `ConvertTo` and `ParseTo` return the default value on expected failures and send internal diagnostics to the configured logger. Unexpected custom-converter exceptions propagate to the caller. Internal diagnostics are independent of the public conditional logging methods.

**Example:**
```csharp
object raw = "123.45";
float val = raw.ConvertTo<float>(); // 123.45f

string input = "Active";
Status status = input.ParseTo<Status>(); // Status.Active
bool parsed = "0".TryParseTo(out int zero); // true, zero == 0
```

---

### 4. Value Conversion System (`ValueConverter` Class)
A caching-based conversion system to minimize parsing overhead for string arrays.

*   **`GatValue<T>(index)`**: Gets and caches the parsed value (Note: method name is `GatValue` in source).
*   **`TryGetValue<T>(index, out result)`**: Returns the cached conversion status and value without logging failures.
*   **`SetValue<T>(index, value)`**: Sets a value and updates the internal string source.

Each slot caches results and success status for every requested type. Updating a slot invalidates all of its type caches. The converter is not thread-safe; callers must synchronize shared access.

**Example:**
```csharp
string[] data = { "100", "True", "2.5" };
var converter = new ValueConverter(data);

int id = converter.GatValue<int>(0); // 100 (parsed and cached)
bool flag = converter.GatValue<bool>(1); // True
```

## Requirements
- **Unity 6.0 or newer**
- **.NET 10.0 SDK** (for compilation; see `global.json`)
- **.NET Standard 2.1** compatibility

## Installation
Install via NuGet or [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity).

## License
This project is licensed under the [MIT License](LICENSE).
