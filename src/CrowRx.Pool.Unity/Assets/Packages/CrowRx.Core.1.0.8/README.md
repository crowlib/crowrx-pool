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

### 2. Enum Extensions (`EnumExtension` Static Class)
High-performance utilities for `System.Enum` types using unsafe operations.

*   **`ToInt<TEnum>()`**: Converts an enum to its integer value at O(1) speed without boxing.
*   **`EqualsEnum(enum2)`**: Compares two enums by their underlying integer values.
*   **`GetAttributeOfType<T>()`**: Retrieves an attribute from an enum member.

**Example:**
```csharp
enum Status { Active = 1, Inactive = 0 }

int value = Status.Active.ToInt(); // 1
bool isActive = Status.Active.EqualsEnum(OtherEnum.One); 
var desc = Status.Active.GetAttributeOfType<DescriptionAttribute>();
```

---

### 3. Object & String Extensions (`ObjectExtension` Static Class)
Robust type conversion and string parsing utilities.

*   **`ConvertTo<T>()`**: Converts an object to type `T` (supports `Nullable` and `InvariantCulture`).
*   **`ParseTo<T>()`**: Parses a string into type `T` (supports primitives, `Enum`, and `Nullable`).

**Example:**
```csharp
object raw = "123.45";
float val = raw.ConvertTo<float>(); // 123.45f

string input = "Active";
Status status = input.ParseTo<Status>(); // Status.Active
```

---

### 4. Value Conversion System (`ValueConverter` Class)
A caching-based conversion system to minimize parsing overhead for string arrays.

*   **`GatValue<T>(index)`**: Gets and caches the parsed value (Note: method name is `GatValue` in source).
*   **`SetValue<T>(index, value)`**: Sets a value and updates the internal string source.

**Example:**
```csharp
string[] data = { "100", "True", "2.5" };
var converter = new ValueConverter(data);

int id = converter.GatValue<int>(0); // 100 (parsed and cached)
bool flag = converter.GatValue<bool>(1); // True
```

## Requirements
- **Unity 6.0 or newer**
- **.NET 8.0 SDK** (for compilation)
- **.NET Standard 2.1** compatibility

## Installation
Install via NuGet or [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity).

## License
This project is licensed under the [MIT License](LICENSE).
