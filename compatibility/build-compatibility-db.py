import argparse
from contextlib import contextmanager
from ctypes import WinDLL, POINTER, byref, c_int, c_uint16, c_uint32, c_uint64, c_void_p, c_wchar_p, create_string_buffer, get_last_error
from pathlib import Path
from uuid import uuid5, NAMESPACE_URL
import os, struct, tempfile, time

DB_NAME = "Watch Dogs 2 Hybrid CPU Compatibility"
SHIM_NAME = "ProcessorCountLieForHybridCPU"
DB_GUID = uuid5(NAMESPACE_URL, "urn:wd2:hybrid-cpu:windows-compatibility:v1")

apphelp = WinDLL(str(Path(os.environ["SystemRoot"]) / "System32" / "apphelp.dll"), use_last_error=True)
def api(name, result, *arguments):
    fn = getattr(apphelp, name)
    fn.restype = result
    fn.argtypes = arguments
    return fn

create = api("SdbCreateDatabase", c_void_p, c_wchar_p, c_int)
close_write = api("SdbCloseDatabaseWrite", None, c_void_p)
begin = api("SdbBeginWriteListTag", c_uint32, c_void_p, c_uint16)
end = api("SdbEndWriteListTag", c_int, c_void_p, c_uint32)
write_string = api("SdbWriteStringTag", c_int, c_void_p, c_uint16, c_wchar_p)
write_dword = api("SdbWriteDWORDTag", c_int, c_void_p, c_uint16, c_uint32)
write_qword = api("SdbWriteQWORDTag", c_int, c_void_p, c_uint16, c_uint64)
write_binary = api("SdbWriteBinaryTag", c_int, c_void_p, c_uint16, c_void_p, c_uint32)
open_database = api("SdbInitDatabase", c_void_p, c_uint32, c_wchar_p)
release_database = api("SdbReleaseDatabase", None, c_void_p)
match_exe = api("SdbGetMatchingExe", c_int, c_void_p, c_wchar_p, c_wchar_p, c_wchar_p, c_uint32, c_void_p)

def checked(result):
    if not result:
        raise OSError(f"AppHelp operation failed, Windows error {get_last_error()}")
    return result

@contextmanager
def list_tag(db, tag):
    item = checked(begin(db, tag))
    yield
    checked(end(db, item))

def text_tag(db, tag, text):
    checked(write_string(db, tag, text))

def number_tag(db, tag, number):
    checked(write_dword(db, tag, number))

def guid_tag(db, tag, value):
    raw = create_string_buffer(value.bytes_le)
    checked(write_binary(db, tag, raw, 16))

def create_database(path, builds):
    db = checked(create(str(path), 0))
    try:
        with list_tag(db, 0x7001):  # DATABASE
            checked(write_qword(db, 0x5001, time.time_ns() // 100 + 116444736000000000))
            text_tag(db, 0x6022, "3.0.0.17")
            text_tag(db, 0x6001, DB_NAME)
            number_tag(db, 0x4021, 2)  # AMD64 runtime platform
            guid_tag(db, 0x9007, DB_GUID)
            number_tag(db, 0x4055, 0)
            with list_tag(db, 0x7002):  # LIBRARY
                pass
            for exe, engine in builds:
                with list_tag(db, 0x7007):  # EXE
                    text_tag(db, 0x6001, exe.name)
                    text_tag(db, 0x6006, "Watch Dogs 2")
                    text_tag(db, 0x6005, "Ubisoft")
                    guid_tag(db, 0x9004, uuid5(DB_GUID, exe.parent.name))
                    guid_tag(db, 0x9011, uuid5(DB_GUID, "Watch Dogs 2"))
                    number_tag(db, 0x4021, 2)
                    for name, size in (("*", exe.stat().st_size), (engine.name, engine.stat().st_size)):
                        with list_tag(db, 0x7008):  # MATCHING_FILE
                            text_tag(db, 0x6001, name)
                            number_tag(db, 0x4001, size)
                    with list_tag(db, 0x7009):  # SHIM_REF
                        text_tag(db, 0x6001, SHIM_NAME)
    finally:
        close_write(db)

def match_count(database, executable):
    result = create_string_buffer(4096)
    matched = match_exe(database, str(executable), None, None, 0, result)
    count = struct.unpack_from("<I", result, 168)[0]
    return count if matched else 0

def validate(path, game, builds):
    database = checked(open_database(2, str(path)))
    try:
        for exe, _ in builds:
            count = match_count(database, exe)
            print(f"{exe.parent.name}/{exe.name}: {count} match(es)")
            if count != 1:
                raise RuntimeError(f"Expected exactly one compatibility entry for {exe}")
        if match_count(database, game / "EAC.exe"):
            raise RuntimeError("Unexpected match for the anti-cheat executable")
        with tempfile.TemporaryDirectory() as scratch:
            same_name = Path(scratch) / builds[0][0].name
            same_name.write_bytes(builds[0][0].read_bytes())
            if match_count(database, same_name):
                raise RuntimeError("Unexpected match for a copy without the engine DLL")
    finally:
        release_database(database)

def main():
    parser = argparse.ArgumentParser(description="Generate the Watch Dogs 2 compatibility database.")
    parser.add_argument("game", type=Path, help="Watch Dogs 2 installation folder")
    parser.add_argument(
        "--output",
        type=Path,
        default=Path(__file__).resolve().with_name("WatchDogs2-HybridCPU.sdb"),
        help="destination SDB file",
    )
    args = parser.parse_args()
    game = args.game.resolve()
    output = args.output.resolve()

    builds = [
        (game / folder / "WatchDogs2.exe", game / folder / "Disrupt_64.dll")
        for folder in ("bin", "bin_plus")
    ]
    for exe, engine in builds:
        for path in (exe, engine):
            if not path.is_file():
                parser.error(f"Required game file not found: {path}")

    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=output.parent) as scratch:
        staged = Path(scratch) / output.name
        create_database(staged, builds)
        validate(staged, game, builds)
        staged.replace(output)
    print(output)


if __name__ == "__main__":
    main()
