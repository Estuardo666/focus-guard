# Java JCS reference provenance

Three files copied from cyberphone/json-canonicalization revision32a48195b9b20fe67b1795e39e4509f14d860b45, java/canonicalizer/src/org/webpki/jcs. All retain Apache-2.0 copyright headers; full license included. Repository source pin, not the old2018 Maven artifact. Verify current upstream and retain notices on distribution.

Local compatibility modification: JsonCanonicalizer.java replaces deprecated/removal-marked new Boolean(token) with Boolean.valueOf(token). Same Boolean.parseBoolean semantics, no number/string canonicalization change; required to retain warnings-as-errors under Java17. Android runtime vectors explicitly include true/false. DoubleCoreSerializer.java and NumberToJSON.java remain unchanged.

Used in the isolated Android spike to compare canonical bytes with Node/C#. No implication that all client codecs/production storage/security review are complete.
