using System.Runtime.InteropServices;

namespace EndfieldChargePlus.Assistant.Native;

/// <summary>Small parameterized wrapper over Windows' SQLite; no extra runtime or database process.</summary>
internal sealed class LocalSqlite : IDisposable
{
    private IntPtr _db;
    public LocalSqlite(string file)
    {
        if (Sql.open(file, out _db, 0x2 | 0x4 | 0x10000, IntPtr.Zero) != 0) { Dispose(); throw new AssistantFailure("usage_database_unavailable"); }
        Exec("PRAGMA busy_timeout=5000; PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL;");
    }
    public void Exec(string sql)
    {
        var code=Sql.exec(_db,sql,IntPtr.Zero,IntPtr.Zero,out var error);
        if(error!=IntPtr.Zero)Sql.free(error);
        if(code!=0)throw new AssistantFailure("local_database_error");
    }
    public List<Dictionary<string,object?>> Query(string sql, params object?[] values)
    {
        if(Sql.prepare(_db,sql,-1,out var statement,IntPtr.Zero)!=0)throw new AssistantFailure("local_database_error");
        try
        {
            for(var i=0;i<values.Length;i++)
            {
                var value=values[i];int result;
                if(value is null)result=Sql.bindNull(statement,i+1);
                else if(value is double or float or decimal)result=Sql.bindDouble(statement,i+1,Convert.ToDouble(value));
                else if(value is int or long or bool)result=Sql.bindInt(statement,i+1,Convert.ToInt64(value));
                else result=Sql.bindText(statement,i+1,Convert.ToString(value)??"",-1,new IntPtr(-1));
                if(result!=0)throw new AssistantFailure("local_database_error");
            }
            var rows=new List<Dictionary<string,object?>>();int code;
            while((code=Sql.step(statement))==100)
            {
                var row=new Dictionary<string,object?>(StringComparer.Ordinal);
                for(var i=0;i<Sql.columnCount(statement);i++)
                {
                    var name=Marshal.PtrToStringUTF8(Sql.columnName(statement,i))!;
                    row[name]=Sql.columnType(statement,i) switch {1=>Sql.columnInt(statement,i),2=>Sql.columnDouble(statement,i),3=>Marshal.PtrToStringUTF8(Sql.columnText(statement,i)),5=>null,_=>throw new AssistantFailure("unsupported_database_value")};
                }
                rows.Add(row);
            }
            if(code!=101)throw new AssistantFailure("local_database_error");
            return rows;
        }
        finally{Sql.finalize(statement);}
    }
    public Dictionary<string,object?>? One(string sql,params object?[] values)=>Query(sql,values).FirstOrDefault();
    public long Number(string sql,params object?[] values)=>Convert.ToInt64(One(sql,values)?.Values.FirstOrDefault()??0);
    public T Transaction<T>(Func<T> action)
    {
        Exec("BEGIN IMMEDIATE");try{var result=action();Exec("COMMIT");return result;}catch{Exec("ROLLBACK");throw;}
    }
    public void Dispose(){if(_db!=IntPtr.Zero){Sql.close(_db);_db=IntPtr.Zero;}}
    private static class Sql
    {
        private const string Lib="winsqlite3.dll";
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport(Lib,EntryPoint="sqlite3_open_v2",CallingConvention=CallingConvention.Cdecl)]internal static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)]string file,out IntPtr db,int flags,IntPtr vfs);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport(Lib,EntryPoint="sqlite3_close_v2",CallingConvention=CallingConvention.Cdecl)]internal static extern int close(IntPtr db);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport(Lib,EntryPoint="sqlite3_exec",CallingConvention=CallingConvention.Cdecl)]internal static extern int exec(IntPtr db,[MarshalAs(UnmanagedType.LPUTF8Str)]string sql,IntPtr callback,IntPtr context,out IntPtr error);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport(Lib,EntryPoint="sqlite3_free",CallingConvention=CallingConvention.Cdecl)]internal static extern void free(IntPtr value);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport(Lib,EntryPoint="sqlite3_prepare_v2",CallingConvention=CallingConvention.Cdecl)]internal static extern int prepare(IntPtr db,[MarshalAs(UnmanagedType.LPUTF8Str)]string sql,int length,out IntPtr statement,IntPtr tail);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport(Lib,EntryPoint="sqlite3_step",CallingConvention=CallingConvention.Cdecl)]internal static extern int step(IntPtr statement);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport(Lib,EntryPoint="sqlite3_finalize",CallingConvention=CallingConvention.Cdecl)]internal static extern int finalize(IntPtr statement);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport(Lib,EntryPoint="sqlite3_bind_null",CallingConvention=CallingConvention.Cdecl)]internal static extern int bindNull(IntPtr s,int i);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport(Lib,EntryPoint="sqlite3_bind_int64",CallingConvention=CallingConvention.Cdecl)]internal static extern int bindInt(IntPtr s,int i,long value);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport(Lib,EntryPoint="sqlite3_bind_double",CallingConvention=CallingConvention.Cdecl)]internal static extern int bindDouble(IntPtr s,int i,double value);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport(Lib,EntryPoint="sqlite3_bind_text",CallingConvention=CallingConvention.Cdecl)]internal static extern int bindText(IntPtr s,int i,[MarshalAs(UnmanagedType.LPUTF8Str)]string value,int length,IntPtr destructor);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport(Lib,EntryPoint="sqlite3_column_count",CallingConvention=CallingConvention.Cdecl)]internal static extern int columnCount(IntPtr s);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport(Lib,EntryPoint="sqlite3_column_name",CallingConvention=CallingConvention.Cdecl)]internal static extern IntPtr columnName(IntPtr s,int i);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport(Lib,EntryPoint="sqlite3_column_type",CallingConvention=CallingConvention.Cdecl)]internal static extern int columnType(IntPtr s,int i);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport(Lib,EntryPoint="sqlite3_column_int64",CallingConvention=CallingConvention.Cdecl)]internal static extern long columnInt(IntPtr s,int i);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport(Lib,EntryPoint="sqlite3_column_double",CallingConvention=CallingConvention.Cdecl)]internal static extern double columnDouble(IntPtr s,int i);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport(Lib,EntryPoint="sqlite3_column_text",CallingConvention=CallingConvention.Cdecl)]internal static extern IntPtr columnText(IntPtr s,int i);
    }
}

internal sealed class AssistantFailure : Exception
{
    public string Code {get;}
    public int Status {get;}
    public AssistantFailure(string code,int status=503):base(code){Code=code;Status=status;}
}
