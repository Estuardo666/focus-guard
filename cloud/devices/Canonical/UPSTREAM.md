# Reference JCS source provenance

Upstream: https://github.com/cyberphone/json-canonicalization
Pinned revision: 32a48195b9b20fe67b1795e39e4509f14d860b45

Eight files from dotnet/jsoncanonicalizer and dotnet/es6numberserializer are copied unchanged, retaining all per-file copyrights and notices. Licenses are mixed: Apache-2.0 canonicalizer/NumberToJson, BSD-style V8 ports, MPL-2.0 NumberDToA with the Lucent/David Gay permissive notice. Full Apache/MPL texts accompany the original inline notices. Do not relicense the complete dependency as Apache-only. This project is not packable and is for interoperability evaluation, not product adoption or a claim of active package maintenance.

The NuGet jsoncanonicalizer1.0.0 package was not adopted because its nuspec did not declare adequate license/source provenance. This reference project disables nullable only for unchanged pre-nullable third-party source; our probe projects retain nullable and warnings-as-errors.
