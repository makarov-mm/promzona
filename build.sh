#!/usr/bin/env bash
# Builds PROMZONA on Linux or macOS, runs the tests and optionally packages or starts the game.
#   ./build.sh [Debug|Release] [--package] [--run]
# Needs CMake 3.25+, a C++23 compiler (GCC 14+, Clang 18+ or Xcode 16+) and git;
# SDL3 is downloaded and built on the first configure.
set -euo pipefail

root="$(cd "$(dirname "$0")" && pwd)"
configuration=Release
package=0
run=0
for arg in "$@"; do
    case "$arg" in
        Debug|Release) configuration="$arg" ;;
        --package) package=1 ;;
        --run) run=1 ;;
        *) echo "Unknown argument: $arg" >&2; exit 2 ;;
    esac
done

build="$root/build/native-$configuration"
generator=()
if command -v ninja >/dev/null 2>&1; then
    generator=(-G Ninja)
fi
jobs="$( (command -v nproc >/dev/null && nproc) || sysctl -n hw.ncpu 2>/dev/null || echo 4)"

# The ${a[@]+...} form keeps an empty array legal under `set -u` in the bash 3.2 of macOS.
cmake -S "$root" -B "$build" ${generator[@]+"${generator[@]}"} -DCMAKE_BUILD_TYPE="$configuration" -DBUILD_TESTING=ON
cmake --build "$build" --parallel "$jobs"
ctest --test-dir "$build" --output-on-failure --parallel "$jobs"

if [[ $package -eq 1 ]]; then
    destination="$root/artifacts/$configuration"
    cmake --install "$build" --prefix "$destination"
    echo "Runtime: $destination/Promzona"
fi
if [[ $run -eq 1 ]]; then
    "$build/Promzona"
fi
echo "Built: $build/Promzona"
