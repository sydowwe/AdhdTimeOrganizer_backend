---
apply: manually
---

in typescript use the shorthand constructor. when its response DTO add method static fromJson(json: any).
when its request dto when the field is nullable set type to type | null and initialize to null when it shouldnt be nullable have the field as field? so it will be initialized as undefined so empty object can be create. Parameter cannot have question mark and initializer so only use question mark when its not nullable
