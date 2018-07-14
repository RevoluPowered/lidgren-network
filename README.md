# Lidgren.Network revised [![Build Status](https://travis-ci.org/RevoluPowered/lidgren-network.svg?branch=master)](https://travis-ci.org/RevoluPowered/lidgren-network)

Includes changes which remove socket poll from being slow, updates API to use socket.Select, this lets you handle 1000+ connections, also it changes the behaviour of the library to use concurrency more and async wherever possible.

The changes are alpha / not production ready, so use them at your own risk for now.

Lidgren.Network is a networking library for .NET framework, which uses a single UDP socket to deliver a simple API for connecting a client to a server, reading and sending messages.

This has been updated for use with Unity3D, feel free to send PRs for other bugs fixes.
To use this in Unity3D just enable the experimental .NET framework.
you can do this in Edit -> Project Settings -> Player -> Other Settings -> Api Compatibility Level -> .NET 4.6

This includes significant performance increases but is not stable yet, this is in alpha, and a lot of work is still in progress.


Platforms supported:
- Linux
- Mac
- OSX

Platforms/Toolchains which need testing:
- Android
- iPhone
- Xamarin

Tested in:
- Mono (alpha and beta)
- .NET 4.6
- Unity 2017.1 -> 2018.x

Future Roadmap:
- Investigate officially supporting .NET Core.
- Improve test suite so that tests are run on all platforms we support, for each release.
