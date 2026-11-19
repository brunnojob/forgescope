# Contributing

Start with a reproducible input and a documented analysis boundary. Add regression cases for false positives, ambiguous imports, missing source, cycles, redaction and malformed input. Never execute target code during a scan or quietly convert advisory evidence into a correctness claim.

Run `python -m unittest discover -s tests -v` with Node 22+ available. Keep vendored engines unchanged unless updating from a reviewed upstream commit; retain licenses and provenance. Avoid adding credentials, personal logs or real proprietary source to examples.
