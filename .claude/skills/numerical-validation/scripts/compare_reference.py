#!/usr/bin/env python3
"""Compare two JSON result files numerically.

Every numeric leaf must agree within the relative OR the absolute tolerance. Non-numeric leaves must be
equal. Structural differences (missing keys, different lengths) are always failures.

Exit codes: 0 agree, 1 differ, 2 bad input.
"""

from __future__ import annotations

import argparse
import json
import math
import sys
from typing import Any, Iterator


def is_number(value: Any) -> bool:
    return isinstance(value, (int, float)) and not isinstance(value, bool)


def number_differences(actual: float, reference: float, rel: float, abs_tol: float, path: str) -> Iterator[str]:
    if math.isnan(actual) or math.isnan(reference):
        if not (math.isnan(actual) and math.isnan(reference)):
            yield f"{path}: actual={actual!r} reference={reference!r} (NaN)"
        return
    if not math.isclose(actual, reference, rel_tol=rel, abs_tol=abs_tol):
        error = actual - reference
        relative = abs(error) / abs(reference) if reference else math.inf
        yield f"{path}: actual={actual!r} reference={reference!r} abs_err={error:.3e} rel_err={relative:.3e}"


def dict_differences(actual: dict, reference: dict, rel: float, abs_tol: float, path: str) -> Iterator[str]:
    for key in sorted(reference.keys() - actual.keys()):
        yield f"{path}.{key}: missing from actual"
    for key in sorted(actual.keys() - reference.keys()):
        yield f"{path}.{key}: not in reference"
    for key in sorted(actual.keys() & reference.keys()):
        yield from differences(actual[key], reference[key], rel, abs_tol, f"{path}.{key}")


def list_differences(actual: list, reference: list, rel: float, abs_tol: float, path: str) -> Iterator[str]:
    if len(actual) != len(reference):
        yield f"{path}: length actual={len(actual)} reference={len(reference)}"
    for index, (a, r) in enumerate(zip(actual, reference)):
        yield from differences(a, r, rel, abs_tol, f"{path}[{index}]")


def differences(actual: Any, reference: Any, rel: float, abs_tol: float, path: str = "$") -> Iterator[str]:
    if is_number(actual) and is_number(reference):
        yield from number_differences(actual, reference, rel, abs_tol, path)
    elif isinstance(actual, dict) and isinstance(reference, dict):
        yield from dict_differences(actual, reference, rel, abs_tol, path)
    elif isinstance(actual, list) and isinstance(reference, list):
        yield from list_differences(actual, reference, rel, abs_tol, path)
    elif type(actual) is not type(reference) or actual != reference:
        yield f"{path}: actual={actual!r} reference={reference!r}"


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("actual")
    parser.add_argument("reference")
    parser.add_argument("--rel", type=float, default=1e-10, help="relative tolerance (default 1e-10)")
    parser.add_argument("--abs", dest="abs_tol", type=float, default=1e-8, help="absolute tolerance (default 1e-8)")
    args = parser.parse_args(argv)

    try:
        with open(args.actual, encoding="utf-8") as handle:
            actual = json.load(handle)
        with open(args.reference, encoding="utf-8") as handle:
            reference = json.load(handle)
    except (OSError, json.JSONDecodeError) as error:
        print(f"cannot read input: {error}", file=sys.stderr)
        return 2

    found = list(differences(actual, reference, args.rel, args.abs_tol))
    for line in found:
        print(line)
    print(f"{len(found)} difference(s) at rel={args.rel:g} abs={args.abs_tol:g}")
    return 1 if found else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
