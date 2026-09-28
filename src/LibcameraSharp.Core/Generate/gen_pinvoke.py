#!/usr/bin/env python3
"""Generate the C# P/Invoke layer from a shim's C headers: the vendored libcamera shim
(src/LibcameraSharp.Native/c_api, the default) or the libtiff shim (src/LibcameraSharp.Native/tiff).

Uses libclang to parse the headers in C mode (so every C++-only typedef collapses to an
opaque struct), then emits:

  Interop/NativeMethods.g.cs        [LibraryImport] for every extern "C" function
  Interop/Types.g.cs         enums, by-value structs, opaque handle structs
  Interop/symbols.txt        sorted export list, diffed against `nm -D` of the built .so
  Interop/layout.g.json      sizeof/offsetof per struct as clang computes them for the target (when there are structs)

Names are kept verbatim from C (bindgen-style): the layer is internal, and 1:1 names make
review against the headers mechanical. The managed layer owns
the idiomatic API.
"""
from __future__ import annotations

import argparse
import json
import shlex
import subprocess
import sys
from pathlib import Path

import clang.cindex as ci
from clang.cindex import CursorKind as CK, TypeKind as TK

# Set from the command line in main(); the defaults generate the libcamera shim's layer.
NAMESPACE = "LibcameraSharp.Native.Interop"
LIB_NAME = "libcamera-shim"
HEADER = ""

SCALARS = {
    TK.VOID: "void", TK.INT: "int", TK.UINT: "uint", TK.LONG: "long", TK.ULONG: "ulong",
    TK.LONGLONG: "long", TK.ULONGLONG: "ulong", TK.SHORT: "short", TK.USHORT: "ushort",
    TK.CHAR_S: "byte", TK.CHAR_U: "byte", TK.SCHAR: "sbyte", TK.UCHAR: "byte",
    TK.FLOAT: "float", TK.DOUBLE: "double", TK.BOOL: "bool",
}
# Fixed-width typedefs and size_t resolve by *spelling* rather than canonical type so that
# the output is identical on every host (size_t is `unsigned long` on LP64, `nuint` in C#).
SPELLED = {
    "size_t": "nuint", "ssize_t": "nint", "uintptr_t": "nuint", "intptr_t": "nint",
    "uint8_t": "byte", "int8_t": "sbyte", "uint16_t": "ushort", "int16_t": "short",
    "uint32_t": "uint", "int32_t": "int", "uint64_t": "ulong", "int64_t": "long",
}


class Model:
    def __init__(self) -> None:
        self.enums: dict[str, list[tuple[str, int]]] = {}
        self.structs: dict[str, list[tuple[str, str]]] = {}   # tag -> [(field, cs_type)]
        self.opaque: set[str] = set()                         # struct tags never defined
        self.typedefs: dict[str, str] = {}                    # typedef name -> C# type name
        self.functions: list[dict] = []
        self.layout: dict[str, dict] = {}
        self.libcamera_version = "?"


def strip_const(t: ci.Type) -> ci.Type:
    return t


def is_char_pointer(t: ci.Type) -> tuple[bool, bool]:
    """(is `char*`, is `const char*`) — the shim's convention: const = borrowed, non-const = caller frees."""
    if t.kind == TK.ELABORATED:
        t = t.get_named_type()
    if t.kind != TK.POINTER:
        return False, False
    pointee = t.get_pointee()
    if pointee.kind == TK.ELABORATED:
        pointee = pointee.get_named_type()
    if pointee.kind not in (TK.CHAR_S, TK.CHAR_U):
        return False, False
    return True, pointee.is_const_qualified()


def cs_type(t: ci.Type, m: Model) -> str:
    """Map a clang type to its C# spelling (unsafe pointers, verbatim C names)."""
    k = t.kind
    if k == TK.ELABORATED:
        return cs_type(t.get_named_type(), m)
    if k == TK.TYPEDEF:
        name = t.get_typedef_name()
        if name in SPELLED:
            return SPELLED[name]
        if name in m.typedefs:
            return m.typedefs[name]
        return cs_type(t.get_canonical(), m)
    if k == TK.POINTER:
        pointee = t.get_pointee()
        pk = pointee.kind
        if pk == TK.ELABORATED:
            pointee = pointee.get_named_type(); pk = pointee.kind
        if pk == TK.TYPEDEF and pointee.get_canonical().kind == TK.FUNCTIONPROTO:
            return fn_pointer(pointee.get_canonical(), m)
        if pk == TK.FUNCTIONPROTO:
            return fn_pointer(pointee, m)
        if pk == TK.VOID:
            return "void*"
        return cs_type(pointee, m) + "*"
    if k in (TK.RECORD,):
        tag = t.get_declaration().spelling
        return tag
    if k == TK.ENUM:
        return t.get_declaration().spelling
    if k in SCALARS:
        return SCALARS[k]
    raise NotImplementedError(f"no C# mapping for {t.spelling} ({k})")


def fn_pointer(proto: ci.Type, m: Model) -> str:
    args = [cs_type(a, m) for a in proto.argument_types()]
    ret = cs_type(proto.get_result(), m)
    return f"delegate* unmanaged[Cdecl]<{', '.join(args + [ret])}>"


def libcamera_version(tu: ci.TranslationUnit) -> str:
    """LIBCAMERA_VERSION_{MAJOR,MINOR,PATCH} from libcamera/version.h, e.g. "0.7.2"."""
    parts = {}
    for c in tu.cursor.get_children():
        if c.kind == CK.MACRO_DEFINITION and c.spelling.startswith("LIBCAMERA_VERSION_"):
            toks = [t.spelling for t in c.get_tokens()]
            if len(toks) == 2 and toks[1].isdigit():
                parts[c.spelling] = toks[1]
    return ".".join(parts.get(f"LIBCAMERA_VERSION_{k}", "?") for k in ("MAJOR", "MINOR", "PATCH"))


def collect(tu: ci.TranslationUnit, api_dir: Path, m: Model) -> None:
    def in_api(c: ci.Cursor) -> bool:
        return c.location.file is not None and Path(c.location.file.name).resolve().parent == api_dir

    # First pass: typedefs and enums, so later type mapping can resolve names.
    for c in tu.cursor.get_children():
        if not in_api(c):
            continue
        if c.kind == CK.ENUM_DECL and c.spelling:
            m.enums[c.spelling] = [(e.spelling, e.enum_value) for e in c.get_children()
                                   if e.kind == CK.ENUM_CONSTANT_DECL]
        elif c.kind == CK.TYPEDEF_DECL:
            under = c.underlying_typedef_type
            u = under.get_named_type() if under.kind == TK.ELABORATED else under
            if u.kind in (TK.RECORD, TK.ENUM):
                m.typedefs[c.spelling] = u.get_declaration().spelling
            elif u.kind == TK.FUNCTIONPROTO:
                pass  # emitted inline as function pointers where used
            else:
                m.typedefs[c.spelling] = cs_type(under, m)

    # Second pass: struct definitions (or lack thereof), functions.
    for c in tu.cursor.get_children():
        if not in_api(c):
            continue
        if c.kind == CK.STRUCT_DECL and c.spelling:
            if c.is_definition():
                fields = [(f.spelling, cs_type(f.type, m)) for f in c.get_children() if f.kind == CK.FIELD_DECL]
                m.structs[c.spelling] = fields
                m.layout[c.spelling] = {
                    "size": c.type.get_size(),
                    "align": c.type.get_align(),
                    "offsets": {f.spelling: c.type.get_offset(f.spelling) // 8
                                for f in c.get_children() if f.kind == CK.FIELD_DECL},
                }
            elif c.spelling not in m.structs:
                m.opaque.add(c.spelling)
        elif c.kind == CK.FUNCTION_DECL:
            params = []
            for p in c.get_arguments():
                name = p.spelling or f"arg{len(params)}"
                is_str, is_const = is_char_pointer(p.type)
                params.append(("string" if is_str and is_const else cs_type(p.type, m), name))
            ret_is_str, ret_is_const = is_char_pointer(c.result_type)
            m.functions.append({
                "name": c.spelling,
                "ret": "string?" if ret_is_str else cs_type(c.result_type, m),
                "ret_borrowed": ret_is_str and ret_is_const,
                "params": params,
                "doc": (c.raw_comment or "").strip(),
                "file": Path(c.location.file.name).name,
            })
    m.opaque -= set(m.structs)


CS_KEYWORDS = {"params", "ref", "out", "in", "string", "object", "event", "base", "fixed", "lock", "checked"}


def ident(name: str) -> str:
    return f"@{name}" if name in CS_KEYWORDS else name


def bool_attr(cs: str, position: str) -> str:
    """C `bool` is one byte; LibraryImport defaults to 4-byte BOOL, so be explicit."""
    if cs != "bool":
        return ""
    return "[return: MarshalAs(UnmanagedType.U1)] " if position == "return" else "[MarshalAs(UnmanagedType.U1)] "


def doc_lines(raw: str) -> list[str]:
    lines = [l.strip().lstrip("/").lstrip("*").strip() for l in raw.splitlines()]
    lines = [l for l in lines if l and l not in ("/", "*")]
    if not lines:
        return []
    return ["/// <summary>"] + [f"/// {l.replace('&', '&amp;').replace('<', '&lt;').replace('>', '&gt;')}" for l in lines] + ["/// </summary>"]


def emit_types(m: Model) -> str:
    out = [HEADER, "using System.Runtime.InteropServices;", "", f"namespace {NAMESPACE};", ""]
    out.append("#pragma warning disable CS1591, CS8981, IDE1006 // generated: verbatim C names")
    out.append("")
    for tag, values in sorted(m.enums.items()):
        out.append(f"internal enum {tag} : int")
        out.append("{")
        for n, v in values:
            out.append(f"    {n} = {v},")
        out.append("}")
        out.append("")
    for tag, fields in sorted(m.structs.items()):
        out.append("[StructLayout(LayoutKind.Sequential)]")
        out.append(f"internal struct {tag}")
        out.append("{")
        for name, cs in fields:
            if cs == "bool":
                out.append("    [MarshalAs(UnmanagedType.U1)]")
            out.append(f"    public {cs} {ident(name)};")
        out.append("}")
        out.append("")
    out.append("// Opaque handles: only ever used through typed pointers, never instantiated.")
    for tag in sorted(m.opaque):
        out.append(f"internal struct {tag} {{ }}")
    out.append("")
    return "\n".join(out)


def emit_native(m: Model) -> str:
    out = [HEADER, "using System.Runtime.InteropServices;", "using System.Runtime.InteropServices.Marshalling;", "", f"namespace {NAMESPACE};", ""]
    out.append("#pragma warning disable CS1591, IDE1006 // generated: verbatim C names")
    out.append("")
    out.append(f"internal static unsafe partial class NativeMethods")
    out.append("{")
    out.append(f'    internal const string LibraryName = "{LIB_NAME}";')
    if m.libcamera_version != "?":
        out.append(f'    /// <summary>libcamera headers these bindings were generated against.</summary>')
        out.append(f'    internal const string GeneratedAgainstLibcamera = "{m.libcamera_version}";')
    current_file = None
    for f in sorted(m.functions, key=lambda f: (f["file"], f["name"])):
        if f["file"] != current_file:
            current_file = f["file"]
            out.append("")
            out.append(f"    // ---- {current_file} ----")
        out.append("")
        for l in doc_lines(f["doc"]):
            out.append(f"    {l}")
        uses_strings = f["ret"] == "string?" or any(cs == "string" for cs, _ in f["params"])
        marshalling = ", StringMarshalling = StringMarshalling.Utf8" if uses_strings else ""
        out.append(f'    [LibraryImport(LibraryName, EntryPoint = "{f["name"]}"{marshalling})]')
        out.append("    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]")
        params = ", ".join(f"{bool_attr(cs, 'param')}{cs} {ident(n)}" for cs, n in f["params"])
        ret_attr = bool_attr(f['ret'], 'return')
        # `const char *` returns belong to libcamera: copy without freeing. `char *` returns are ours to free (the default Utf8 marshaller does).
        if f.get("ret_borrowed"):
            ret_attr = "[return: MarshalUsing(typeof(BorrowedUtf8StringMarshaller))] "
        out.append(f"    {ret_attr}internal static partial {f['ret']} {f['name']}({params});")
    out.append("}")
    out.append("")
    return "\n".join(out)


def main() -> int:
    global NAMESPACE, LIB_NAME, HEADER
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--api-dir", type=Path, default=Path(__file__).resolve().parents[3] / "src" / "LibcameraSharp.Native" / "c_api")
    ap.add_argument("--out-dir", type=Path, default=Path(__file__).resolve().parents[3] / "src" / "LibcameraSharp.Core" / "Native" / "Interop")
    ap.add_argument("--target", default="aarch64-linux-gnu", help="clang target triple for layout computation")
    ap.add_argument("--libclang", default=None, help="path to libclang.so if not discoverable")
    ap.add_argument("--cflags", default=None, help="extra compiler flags; default: `pkg-config --cflags libcamera`")
    ap.add_argument("--library", default=LIB_NAME, help="the shim's library name, as [LibraryImport] names it")
    ap.add_argument("--namespace", default=NAMESPACE, help="C# namespace of the generated files")
    args = ap.parse_args()
    NAMESPACE, LIB_NAME = args.namespace, args.library
    repo = Path(__file__).resolve().parents[3]
    HEADER = (f"// <auto-generated> by src/LibcameraSharp.Core/Generate/gen_pinvoke.py from "
              f"{args.api_dir.resolve().relative_to(repo)} — do not edit. </auto-generated>\n#nullable enable\n")
    cflags = shlex.split(args.cflags) if args.cflags is not None else \
        shlex.split(subprocess.check_output(["pkg-config", "--cflags", "libcamera"], text=True))

    if args.libclang:
        ci.Config.set_library_file(args.libclang)
    api_dir = args.api_dir.resolve()
    headers = sorted(p for p in api_dir.glob("*.h"))
    umbrella = "\n".join(f'#include "{h.name}"' for h in headers) + "\n"

    index = ci.Index.create()
    tu = index.parse("umbrella.c", args=["-x", "c", "-std=c11", f"-I{api_dir}", f"--target={args.target}", *cflags],
                     unsaved_files=[("umbrella.c", umbrella)],
                     options=ci.TranslationUnit.PARSE_DETAILED_PROCESSING_RECORD)
    errors = [d for d in tu.diagnostics if d.severity >= ci.Diagnostic.Error]
    if errors:
        for d in errors:
            print(d, file=sys.stderr)
        return 1

    m = Model()
    collect(tu, api_dir, m)
    m.libcamera_version = libcamera_version(tu)

    args.out_dir.mkdir(parents=True, exist_ok=True)
    (args.out_dir / "Types.g.cs").write_text(emit_types(m))
    (args.out_dir / "NativeMethods.g.cs").write_text(emit_native(m))
    (args.out_dir / "symbols.txt").write_text("\n".join(sorted(f["name"] for f in m.functions)) + "\n")
    # LayoutTests checks these; a shim with no by-value structs has nothing to check.
    if m.layout:
        (args.out_dir / "layout.g.json").write_text(json.dumps({"target": args.target, "structs": m.layout}, indent=2, sort_keys=True) + "\n")
    print(f"{len(m.functions)} functions, {len(m.enums)} enums, {len(m.structs)} structs, {len(m.opaque)} opaque types "
          f"-> {args.out_dir}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
