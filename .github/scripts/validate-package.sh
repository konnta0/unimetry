#!/usr/bin/env bash
set -euo pipefail

package_name="$(jq -er '.name' package.json)"
package_version="$(jq -er '.version' package.json)"
unity_version="$(jq -er '.unity' package.json)"

if [[ "$package_name" != "com.konnta0.unimetry" ]]; then
  echo "Unexpected package name: $package_name" >&2
  exit 1
fi

if [[ ! "$package_version" =~ ^[0-9]+\.[0-9]+\.[0-9]+([+-][0-9A-Za-z.-]+)?$ ]]; then
  echo "Invalid package version: $package_version" >&2
  exit 1
fi

if [[ ! "$unity_version" =~ ^[0-9]{4}\.[0-9]+$ ]]; then
  echo "Invalid minimum Unity version: $unity_version" >&2
  exit 1
fi

grep -Fq "Current scope (v$package_version)" README.md
grep -Fq "現状のスコープ (v$package_version)" README.ja.md

scope_version_count="$(grep -Fc "version\\\":\\\"$package_version" Runtime/Internal/OtlpJsonWriter.cs)"
if [[ "$scope_version_count" -ne 3 ]]; then
  echo "Expected three OTLP scope version entries for $package_version, found $scope_version_count" >&2
  exit 1
fi

grep -Fq "[\"telemetry.sdk.version\"] = \"$package_version\"" Runtime/Internal/ResourceSnapshot.cs

missing_meta=0
while IFS= read -r asset; do
  if [[ "$asset" == *.meta ]]; then
    continue
  fi

  if [[ ! -f "$asset.meta" ]]; then
    echo "Missing Unity meta file: $asset.meta" >&2
    missing_meta=1
  fi
done < <(git ls-files Runtime Editor Plugins)

if [[ "$missing_meta" -ne 0 ]]; then
  exit 1
fi

duplicate_guids="$(git ls-files -z '*.meta' | xargs -0 awk '/^guid: / { print $2 }' | sort | uniq -d)"
if [[ -n "$duplicate_guids" ]]; then
  echo "Duplicate Unity meta GUIDs:" >&2
  echo "$duplicate_guids" >&2
  exit 1
fi

echo "Validated $package_name $package_version (Unity $unity_version)."
