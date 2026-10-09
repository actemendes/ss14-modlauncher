# Third-party notices

This application includes the following components. Their licenses apply independently of the project's [MIT license](LICENSE).

| Component | Version | License / source |
| --- | --- | --- |
| Harmony / Lib.Harmony | 2.4.2 | MIT; [upstream](https://github.com/pardeike/Harmony), [included license](docs/licenses/Harmony-LICENSE.txt) |
| Mono.Cecil | 0.11.6 | MIT; [upstream](https://github.com/jbevain/cecil), [included license](docs/licenses/Mono.Cecil-LICENSE.txt) |
| Microsoft .NET runtime and Windows Desktop runtime | Selected by .NET SDK 10 at publish time; recorded in build metadata | [dotnet/runtime](https://github.com/dotnet/runtime), [dotnet/windowsdesktop](https://github.com/dotnet/windowsdesktop); runtime license and notices copied into the release's `licenses/` directory |

The build takes runtime notices from the exact restored runtime packages. The ZIP includes those files along with this document. The .NET notices cover bundled runtime third-party components.

Original SS14 launcher files, Robust assemblies, and server content are not distributed in the package. SS14 / Space Station 14 names are used to describe compatibility. This independent project does not claim endorsement from the original developers.

The separate Crew Monitor project is the design reference. The preserved [port research](docs/crew-monitor-port.md) identifies its studied revision and the boundary between implemented and planned features.
