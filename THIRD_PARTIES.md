# Third-party notices

The source code of hanautaSketch is licensed under the MIT License (see [LICENSE](LICENSE)).
This file lists the data and libraries that come from third parties, and the files in this
repository that are under a license other than MIT.

## Files under other licenses

| Path | License |
| --- | --- |
| `HanautaSketch.Engine/Smart/Chords/Data/chord-transitions.json` | CC BY-NC 4.0 (derived from Chordonomicon; see below) |
| `HanautaSketch.Tests/Fixtures/*.hsk` | Public domain ([CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/)) |

`chord-transitions.json` contains chord transition counts aggregated from the datasets below
by `tools/ChordStats/ChordStats.cs`. Because one of the sources is licensed under CC BY-NC 4.0,
this file — and any build of hanautaSketch that embeds it — may be used and redistributed
for **non-commercial purposes only**. To use the rest of the code commercially, remove the file
or regenerate it from data whose license allows commercial use.

## Datasets

### McGill Billboard Project

- Used for: `chord-transitions.json`
- License: [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/)
- Source: <https://ddmal.music.mcgill.ca/research/The_McGill_Billboard_Project_(Chord_Analysis_Dataset)>
- Reference: J. A. Burgoyne, J. Wild, and I. Fujinaga, "An Expert Ground Truth Set for Audio Chord
  Recognition and Music Analysis," in *Proceedings of the 12th International Society for Music
  Information Retrieval Conference (ISMIR)*, 2011.

### Chordonomicon

- Used for: `chord-transitions.json`
- License: [CC BY-NC 4.0](https://creativecommons.org/licenses/by-nc/4.0/)
- Source: <https://huggingface.co/datasets/ailsntua/Chordonomicon>
- Reference: S. Kantarelis, K. Thomas, V. Lyberatos, E. Dervakos, and G. Stamou,
  "CHORDONOMICON: A Dataset of 666,000 Songs and their Chord Progressions,"
  arXiv:2410.22046, 2024.
- Changes: only aggregated counts of chord-to-chord transitions (relative to an estimated key)
  are included; no chord progressions of individual songs are redistributed.

The original data of both datasets is downloaded by the tool into `tools/ChordStats/.cache/`
and is not included in this repository.

## Libraries

Distributed with the application:

| Package | License |
| --- | --- |
| [Avalonia](https://github.com/AvaloniaUI/Avalonia) (Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent) | MIT |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | MIT |
| [MeltySynth](https://github.com/sinshu/meltysynth) | MIT |
| [NAudio](https://github.com/naudio/NAudio) (NAudio.Wasapi) | MIT |

Used only for tests (not distributed):

| Package | License |
| --- | --- |
| [DryWetMIDI](https://github.com/melanchall/drywetmidi) | MIT |
| [xUnit.net](https://github.com/xunit/xunit) (xunit, xunit.runner.visualstudio) | Apache-2.0 |
| [Microsoft.NET.Test.Sdk](https://github.com/microsoft/vstest) | MIT |
| [coverlet.collector](https://github.com/coverlet-coverage/coverlet) | MIT |

## SoundFont

No SoundFont is included in this repository. Place a GM-compatible `.sf2` file in
`HanautaSketch/SoundFonts/` yourself, and follow the license of the SoundFont you use.
