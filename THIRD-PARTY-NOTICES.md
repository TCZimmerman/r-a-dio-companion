# Third-party notices

RadioCompanion's LICENSE applies only to its original source code. The
following components and their bundled dependencies and modules retain their
respective upstream licences. The ordinary VideoLAN.LibVLC.Windows package is
used; the separate VideoLAN.LibVLC.Windows.GPL package is not used.

| Component | Version | Upstream information |
| --- | --- | --- |
| LibVLCSharp | 3.10.1 | [Source at the package revision](https://github.com/videolan/libvlcsharp/tree/4896d0e06d19ac40b46802cf7dc167d432f3fd63); [LGPL 2.1 or later](https://www.nuget.org/packages/LibVLCSharp/3.10.1) |
| VideoLAN.LibVLC.Windows / LibVLC | 3.0.23.1 / 3.0.23; bundled binaries identify VLC revision `79128878dd` | [Package](https://www.nuget.org/packages/VideoLAN.LibVLC.Windows/3.0.23.1); [VLC source at the identified revision](https://github.com/videolan/vlc/tree/79128878ddb2c280bbb6c89c76a46b31a80ade1c); [VLC 3.0.23 source archive](https://download.videolan.org/pub/videolan/vlc/3.0.23/vlc-3.0.23.tar.xz) |
| FFmpeg bundled in VLC plugins | 4.4.5 | [VideoLAN's FFmpeg 4.4.5 source archive](https://download.videolan.org/pub/contrib/ffmpeg/ffmpeg-4.4.5.tar.xz); [FFmpeg licensing information](https://ffmpeg.org/legal.html) |

The package metadata identifies VideoLAN.LibVLC.Windows as LGPL 2.1 or later,
but that classification does not describe every bundled plugin or dependency.
Some bundled VLC modules identify themselves as GPL 2 or later, and the
bundled FFmpeg build reports `--enable-gpl`. The upstream GPL version 2 and
LGPL version 2.1 licence texts are supplied in `third-party-licenses/`.
Copyright and licence notices within third-party files remain applicable.

[VideoLAN's redistribution guidance](https://docs.videolan.me/vlc-user/en/support/faq/legalconcerns.html)
discusses libVLC applications, GPL modules, and linking to VLC sources for
website downloads. The source links above identify upstream releases and
revisions; they have not been independently verified as a complete
corresponding-source bundle for every binary in this package.
