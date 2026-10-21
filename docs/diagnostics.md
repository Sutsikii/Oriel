# Generator diagnostics

The source generator reports errors when a service cannot be generated as
written. Each is reported where the problem is: the parameter, the property
or the class. Methods and types that are fine are generated anyway.

## ORIEL001: Type not supported by Oriel

A parameter, a result, a property or an event payload has a type that cannot
be sent between C# and pages. The message says what has the type and why:

```
parameter 'link' of Decks.Open cannot be sent between C# and pages: System.Uri is not a type Oriel maps
property Deck.Owner cannot be sent between C# and pages: IUser is an interface; Oriel sends concrete classes, records and structs
```

Use one of the types listed in [bindings.md](bindings.md#types): for
instance a `string` for a URI, or a record for a tuple. A type that is not
supported is reported once, where it is first used.

## ORIEL002: Class cannot be a service

A `[Service]` class is static, abstract, generic, not accessible to
generated code (private, or nested in a private class), or its
`[Service(Name = ...)]` is not an identifier.

## ORIEL003: Two definitions share a name in TypeScript

The client names types and services by their short names. Two services of
one name, or two types of one name in different namespaces, would clash.
Rename one, or give a service another name with `[Service(Name = "...")]`.

## ORIEL004: Overloaded service method

JavaScript cannot tell overloads apart. Rename one of the methods, or hide it
from pages with `[ServiceIgnore]`. Optional parameters often replace
overloads.

## ORIEL005: Method cannot be called by pages

A public method is generic, or has `ref`, `out` or `in` parameters. Change
it, or hide it with `[ServiceIgnore]`.
