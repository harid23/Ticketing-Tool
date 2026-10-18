#!/usr/bin/env bash
# Creates the .sln file and wires the projects together. Run once from the repo root.
set -e
dotnet new sln -n ReleaseTicketing
dotnet sln add src/ReleaseTicketing.Core src/ReleaseTicketing.Api src/ReleaseTicketing.Functions tests/ReleaseTicketing.Tests
