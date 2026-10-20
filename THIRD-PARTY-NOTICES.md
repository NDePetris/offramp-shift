# Persistence dependency notices

Development licensing remains GPLv3; these dependencies do not select a public
distribution model. Before distributing, retain all applicable upstream license
and notice files for the actual runtime assets included.

| Dependency | Locked version | Upstream license / source |
|---|---|---|
| Microsoft.Data.Sqlite, Microsoft.Data.Sqlite.Core | 10.0.12 | MIT; Copyright Microsoft Corporation. [Source](https://github.com/dotnet/efcore), [license](https://github.com/dotnet/efcore/blob/main/LICENSE.txt) |
| SQLitePCLRaw.bundle_e_sqlite3, core, provider.e_sqlite3, lib.e_sqlite3 | 2.1.12 | Apache-2.0; Copyright 2014–2024 SourceGear, LLC. [Source and license](https://github.com/ericsink/SQLitePCL.raw) |
| Native SQLite bundled by lib.e_sqlite3 | Supplied by locked native package | [SQLite public-domain dedication](https://www.sqlite.org/copyright.html) |

Versions, hashes and resolved runtime dependency graphs are recorded in
`src/OfframpShift.Persistence/packages.lock.json` and
`tests/OfframpShift.Persistence.Tests/packages.lock.json`. The net10.0 graph
introduces only the packages above. No system SQLite installation or ORM is used.
Package restore and vulnerability audit use NuGet during development/CI;
application runtime makes no network requests.

The shared `OfframpShift.Switching` integration was extracted from the
virtual-desktop spike, with the same pinned ABI source and unchanged
[third-party notices](spikes/VirtualDesktop/THIRD-PARTY-NOTICES.md).
