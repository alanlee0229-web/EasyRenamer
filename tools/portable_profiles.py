from __future__ import annotations


CANONICAL_PUBLIC_PROFILE = "compact"

PORTABLE_PROFILES: dict[str, dict[str, str]] = {
    "compact": {
        "PublishSingleFile": "true",
        "PublishTrimmed": "false",
        "PublishReadyToRun": "false",
        "IncludeNativeLibrariesForSelfExtract": "true",
        "EnableCompressionInSingleFile": "true",
        "DebugType": "None",
        "DebugSymbols": "false",
    },
    "fast": {
        "PublishSingleFile": "true",
        "PublishTrimmed": "false",
        "PublishReadyToRun": "true",
        "IncludeNativeLibrariesForSelfExtract": "true",
        "EnableCompressionInSingleFile": "false",
        "DebugType": "None",
        "DebugSymbols": "false",
    },
}


def msbuild_property_args(profile: str) -> list[str]:
    try:
        properties = PORTABLE_PROFILES[profile]
    except KeyError as exc:
        raise ValueError(f"Unknown portable profile: {profile}") from exc
    return [f"-p:{name}={value}" for name, value in properties.items()]
