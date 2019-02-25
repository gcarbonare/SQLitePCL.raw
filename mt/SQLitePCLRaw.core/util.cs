﻿/*
   Copyright 2014-2019 Zumero, LLC

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
*/

// Copyright © Microsoft Open Technologies, Inc.
// All Rights Reserved
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at 
// http://www.apache.org/licenses/LICENSE-2.0
// 
// THIS CODE IS PROVIDED ON AN *AS IS* BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION ANY IMPLIED WARRANTIES OR CONDITIONS OF TITLE, FITNESS FOR A PARTICULAR PURPOSE, MERCHANTABLITY OR NON-INFRINGEMENT.
// 
// See the Apache 2 License for the specific language governing permissions and limitations under the License.

namespace SQLitePCL
{
    using System;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Collections.Generic;

    internal static class hooks
    {
	    internal class info
	    {
		    // TODO note that sqlite function names can be case-insensitive.  but we're using
		    // a dictionary with a string key to keep track of them.  this has the potential
		    // to cause problems.  fixing it with a case-insensitive string comparer is not
		    // correct here, since the .NET notion of case-insensitivity is different (more
		    // complete) than SQLite's notion.

			public Dictionary<string, collation_hook_handle> collation = new Dictionary<string, collation_hook_handle>();
			public Dictionary<string, scalar_function_hook_handle> scalar = new Dictionary<string, scalar_function_hook_handle>();
			public Dictionary<string, agg_function_hook_handle> agg = new Dictionary<string, agg_function_hook_handle>();
			public update_hook_handle update;
			public rollback_hook_handle rollback;
			public commit_hook_handle commit;
			public trace_hook_handle trace;
			public progress_handler_hook_handle progress;
			public profile_hook_handle profile;
            public authorizer_hook_handle authorizer;

		    public void Dispose()
		    {
			foreach (var h in collation.Values) h.Dispose();
			foreach (var h in scalar.Values) h.Dispose();
			foreach (var h in agg.Values) h.Dispose();
			if (update!=null) update.Dispose();
			if (rollback!=null) rollback.Dispose();
			if (commit!=null) commit.Dispose();
			if (trace!=null) trace.Dispose();
			if (progress!=null) progress.Dispose();
			if (profile!=null) profile.Dispose();
			if (authorizer!=null) authorizer.Dispose();
		    }
	    }

        internal static log_hook_handle log;

        private static System.Collections.Concurrent.ConcurrentDictionary<IntPtr,info> _hooks_by_db = new System.Collections.Concurrent.ConcurrentDictionary<IntPtr,info>();

	internal static info getOrCreateFor(IntPtr db)
	{
		info i;
                i = _hooks_by_db.GetOrAdd(db, (unused) => new info());
                return i;
	}

	internal static void removeFor(IntPtr db)
	{
		info i;
                if (_hooks_by_db.TryRemove(db, out i))
                {
	            i.Dispose();
                }
	}
    }

    internal static class util
    {
        internal static byte[] to_utf8(this string sourceText)
        {
            if (sourceText == null)
            {
                return null;
            }

            byte[] byteArray;
            int nlen = Encoding.UTF8.GetByteCount(sourceText) + 1;

            byteArray = new byte[nlen];
            nlen = Encoding.UTF8.GetBytes(sourceText, 0, sourceText.Length, byteArray, 0);
            byteArray[nlen] = 0;

            return byteArray;
        }

        private static int GetNativeUTF8Size(System.IntPtr nativeString)
        {
            var offset = 0;

            if (nativeString != IntPtr.Zero)
            {
                while (Marshal.ReadByte(nativeString, offset) > 0)
                {
                    offset++;
                }

                offset++;
            }

            return offset;
        }

        internal static string from_utf8(IntPtr nativeString)
        {
            string result = null;

            if (nativeString != IntPtr.Zero)
            {
                int size = GetNativeUTF8Size(nativeString);
                var array = new byte[size - 1];
                Marshal.Copy(nativeString, array, 0, size - 1);
                result = Encoding.UTF8.GetString(array, 0, array.Length);
            }

            return result;
        }

        internal static string from_utf8(IntPtr nativeString, int size)
        {
            string result = null;

            if (nativeString != IntPtr.Zero)
            {
                var array = new byte[size];
                Marshal.Copy(nativeString, array, 0, size);
                result = Encoding.UTF8.GetString(array, 0, array.Length);
            }

            return result;
        }
    }

    internal class log_hook_info
    {
        private delegate_log _func;
        private object _user_data;

        internal log_hook_info(delegate_log func, object v)
        {
            _func = func;
            _user_data = v;
        }

        internal static log_hook_info from_ptr(IntPtr p)
        {
            GCHandle h = (GCHandle) p;
            log_hook_info hi = h.Target as log_hook_info;
            return hi;
        }

        internal void call(int rc, string msg)
        {
            _func(_user_data, rc, msg);
        }
    }

    internal class log_hook_handle : hook_handle
    {
        internal log_hook_handle(delegate_log func, object v)
			: base(new log_hook_info(func, v))
        {
        }
    }

    internal class commit_hook_info
    {
        public delegate_commit _func { get; private set; }
        public object _user_data { get; private set; }

        public commit_hook_info(delegate_commit func, object v)
        {
            _func = func;
            _user_data = v;
        }

        internal int call()
        {
            return _func(_user_data);
        }

        internal static commit_hook_info from_ptr(IntPtr p)
        {
            GCHandle h = (GCHandle) p;
            commit_hook_info hi = h.Target as commit_hook_info;
            return hi;
        }
    }

    internal class hook_handle : IDisposable
    {
		IntPtr _p;

        internal hook_handle(object target)
        {
			var h = GCHandle.Alloc(target);
			_p = GCHandle.ToIntPtr(h);
        }

        internal IntPtr ptr => _p;

        public void Dispose()
        {
			var h = GCHandle.FromIntPtr(_p);
			h.Free();
			_p = IntPtr.Zero;
        }
    }

    internal class commit_hook_handle : hook_handle
    {
        internal commit_hook_handle(delegate_commit func, object v)
			: base(new commit_hook_info(func, v))
        {
        }
    }

    internal class rollback_hook_info
    {
        private delegate_rollback _func;
        private object _user_data;

        internal rollback_hook_info(delegate_rollback func, object v)
        {
            _func = func;
            _user_data = v;
        }

        internal static rollback_hook_info from_ptr(IntPtr p)
        {
            GCHandle h = (GCHandle) p;
            rollback_hook_info hi = h.Target as rollback_hook_info;
            // TODO assert(hi._h == h)
            return hi;
        }

        internal void call()
        {
            _func(_user_data);
        }
    }

    internal class rollback_hook_handle : hook_handle
    {
        internal rollback_hook_handle(delegate_rollback func, object v)
			: base(new rollback_hook_info(func, v))
        {
        }
    }

    internal class trace_hook_info
    {
        private delegate_trace _func;
        private object _user_data;

        internal trace_hook_info(delegate_trace func, object v)
        {
            _func = func;
            _user_data = v;
        }

        internal static trace_hook_info from_ptr(IntPtr p)
        {
            GCHandle h = (GCHandle) p;
            trace_hook_info hi = h.Target as trace_hook_info;
            return hi;
        }

        internal void call(string s)
        {
            _func(_user_data, s);
        }
    }

    internal class trace_hook_handle : hook_handle
    {
        internal trace_hook_handle(delegate_trace func, object v)
			: base(new trace_hook_info(func, v))
        {
        }
    }

    internal class profile_hook_info
    {
        private delegate_profile _func;
        private object _user_data;

        internal profile_hook_info(delegate_profile func, object v)
        {
            _func = func;
            _user_data = v;
        }

        internal static profile_hook_info from_ptr(IntPtr p)
        {
            GCHandle h = (GCHandle) p;
            profile_hook_info hi = h.Target as profile_hook_info;
            return hi;
        }

        internal void call(string s, long elapsed)
        {
            _func(_user_data, s, elapsed);
        }
    }

    internal class profile_hook_handle : hook_handle
    {
        internal profile_hook_handle(delegate_profile func, object v)
			: base(new profile_hook_info(func, v))
        {
        }
    }

    internal class progress_handler_hook_info
    {
        private delegate_progress_handler _func;
        private object _user_data;

        internal progress_handler_hook_info(delegate_progress_handler func, object v)
        {
            _func = func;
            _user_data = v;
        }

        internal static progress_handler_hook_info from_ptr(IntPtr p)
        {
            GCHandle h = (GCHandle)p;
            progress_handler_hook_info hi = h.Target as progress_handler_hook_info;
            return hi;
        }

        internal int call()
        {
            return _func(_user_data);
        }
    }

    internal class progress_handler_hook_handle : hook_handle
    {
        internal progress_handler_hook_handle(delegate_progress_handler func, object v)
			: base(new progress_handler_hook_info(func, v))
        {
        }
    }

    internal class update_hook_info
    {
        private delegate_update _func;
        private object _user_data;

        internal update_hook_info(delegate_update func, object v)
        {
            _func = func;
            _user_data = v;
        }

        internal static update_hook_info from_ptr(IntPtr p)
        {
            GCHandle h = (GCHandle) p;
            update_hook_info hi = h.Target as update_hook_info;
            return hi;
        }

        internal void call(int typ, string db, string tbl, long rowid)
        {
            _func(_user_data, typ, db, tbl, rowid);
        }
    }

    internal class update_hook_handle : hook_handle
    {
        internal update_hook_handle(delegate_update func, object v)
			: base(new update_hook_info(func, v))
        {
        }
    }

    internal class collation_hook_info
    {
        private delegate_collation _func;
        private object _user_data;

        internal collation_hook_info(delegate_collation func, object v)
        {
            _func = func;
            _user_data = v;
        }

        internal static collation_hook_info from_ptr(IntPtr p)
        {
            GCHandle h = (GCHandle) p;
            collation_hook_info hi = h.Target as collation_hook_info;
            return hi;
        }

        internal int call(string s1, string s2)
        {
            return _func(_user_data, s1, s2);
        }
    }

    internal class collation_hook_handle : hook_handle
    {
        internal collation_hook_handle(delegate_collation func, object v)
			: base(new collation_hook_info(func, v))
        {
        }
    }

    internal class exec_hook_info
    {
        private delegate_exec _func;
        private object _user_data;

        internal exec_hook_info(delegate_exec func, object v)
        {
            _func = func;
            _user_data = v;
        }

        internal static exec_hook_info from_ptr(IntPtr p)
        {
            GCHandle h = (GCHandle) p;
            exec_hook_info hi = h.Target as exec_hook_info;
            return hi;
        }

        internal int call(int n, IntPtr values_ptr, IntPtr names_ptr)
        {
            string[] values = new string[n];
            string[] names = new string[n];
            // TODO warning on the following line.  SizeOf(Type) replaced in .NET 4.5.1 with SizeOf<T>()
            int ptr_size = Marshal.SizeOf(typeof(IntPtr));
            for (int i=0; i<n; i++)
            {
                IntPtr vp;

                vp = Marshal.ReadIntPtr(values_ptr, i * ptr_size);
                values[i] = util.from_utf8(vp);

                vp = Marshal.ReadIntPtr(names_ptr, i * ptr_size);
                names[i] = util.from_utf8(vp);
            }

            return _func(_user_data, values, names);
        }
    }

    internal class exec_hook_handle : hook_handle
    {
        internal exec_hook_handle(delegate_exec func, object v)
			: base(new exec_hook_info(func, v))
        {
        }
    }

    internal class scalar_function_hook_info
    {
        private delegate_function_scalar _func;
        private object _user_data;

        internal scalar_function_hook_info(delegate_function_scalar func, object v)
        {
            _func = func;
            _user_data = v;
        }

        internal static scalar_function_hook_info from_ptr(IntPtr p)
        {
            GCHandle h = (GCHandle) p;
            scalar_function_hook_info hi = h.Target as scalar_function_hook_info;
            return hi;
        }

        private class scalar_sqlite3_context : sqlite3_context
        {
            public scalar_sqlite3_context(IntPtr p, object v) : base(v)
            {
                set_context_ptr(p);
            }
        }

        internal void call(IntPtr context, int num_args, IntPtr argsptr)
        {
            scalar_sqlite3_context ctx = new scalar_sqlite3_context(context, _user_data);

            sqlite3_value[] a = new sqlite3_value[num_args];
            // TODO warning on the following line.  SizeOf(Type) replaced in .NET 4.5.1 with SizeOf<T>()
            int ptr_size = Marshal.SizeOf(typeof(IntPtr));
            for (int i=0; i<num_args; i++)
            {
                IntPtr vp = Marshal.ReadIntPtr(argsptr, i * ptr_size);
                a[i] = new sqlite3_value(vp);
            }

            _func(ctx, _user_data, a);
        }
    }

    internal class scalar_function_hook_handle : hook_handle
    {
        internal scalar_function_hook_handle(delegate_function_scalar func, object v)
			: base(new scalar_function_hook_info(func, v))
        {
        }
    }

    internal class agg_function_hook_info
    {
        private delegate_function_aggregate_step _func_step;
        private delegate_function_aggregate_final _func_final;
        private object _user_data;

        private class agg_sqlite3_context : sqlite3_context
        {
            public agg_sqlite3_context(object v) : base(v)
            {
            }

            public void fix_ptr(IntPtr p)
            {
                set_context_ptr(p);
            }
        }

        internal agg_function_hook_info(delegate_function_aggregate_step func_step, delegate_function_aggregate_final func_final, object user_data)
        {
            _func_step = func_step;
            _func_final = func_final;
            _user_data = user_data;
        }

        internal static agg_function_hook_info from_ptr(IntPtr p)
        {
            GCHandle h = (GCHandle) p;
            agg_function_hook_info hi = h.Target as agg_function_hook_info;
            return hi;
        }

        private sqlite3_context get_context(IntPtr context, IntPtr agg_context)
        {
            // agg_context is a pointer to 8 bytes of storage, obtained from
            // sqlite3_aggregate_context().  we will use it to store the
            // sqlite3_context object so we can use the same object for all
            // calls to xStep and xFinal for this instance/invocation of the
            // aggregate function.

            agg_sqlite3_context ctx;
            IntPtr c = Marshal.ReadIntPtr(agg_context);
            if (c == IntPtr.Zero)
            {
                // this is the first call to xStep or xFinal.  we need a new
                // sqlite3_context object to pass to the user's callback.
                ctx = new agg_sqlite3_context(_user_data);

                // and store a handle in the agg_context storage area so we
                // can get this back next time.
                GCHandle h = GCHandle.Alloc(ctx);
                Marshal.WriteIntPtr(agg_context, (IntPtr) h);
            }
            else
            {
                // we've been through here before.  retrieve the sqlite3_context
                // object from the agg_context storage area.
                GCHandle h = (GCHandle) c;
                ctx = h.Target as agg_sqlite3_context;
            }

            // we are reusing the same sqlite3_context object for each call
            // to xStep/xFinal within the same instance/invocation of the
            // user's agg function.  but SQLite actually gives us a different
            // context pointer on each call.  so we need to fix it up.
            ctx.fix_ptr(context);

            return ctx;
        }

        internal void call_step(IntPtr context, IntPtr agg_context, int num_args, IntPtr argsptr)
        {
            sqlite3_context ctx = get_context(context, agg_context);

            sqlite3_value[] a = new sqlite3_value[num_args];
            // TODO warning on the following line.  SizeOf(Type) replaced in .NET 4.5.1 with SizeOf<T>()
            int ptr_size = Marshal.SizeOf(typeof(IntPtr));
            for (int i=0; i<num_args; i++)
            {
                IntPtr vp = Marshal.ReadIntPtr(argsptr, i * ptr_size);
                a[i] = new sqlite3_value(vp);
            }

            _func_step(ctx, _user_data, a);
        }

        internal void call_final(IntPtr context, IntPtr agg_context)
        {
            sqlite3_context ctx = get_context(context, agg_context);

            _func_final(ctx, _user_data);

            IntPtr c = Marshal.ReadIntPtr(agg_context);
            GCHandle h = (GCHandle) c;
            h.Free();
        }

    }

    internal class agg_function_hook_handle : hook_handle
    {
        internal agg_function_hook_handle(delegate_function_aggregate_step func_step, delegate_function_aggregate_final func_final, object v)
			: base(new agg_function_hook_info(func_step, func_final, v))
        {
        }
    }

    internal class authorizer_hook_info
    {
        private delegate_authorizer _func;
        private object _user_data;

        internal authorizer_hook_info(delegate_authorizer func, object v)
        {
            _func = func;
            _user_data = v;
        }

        internal static authorizer_hook_info from_ptr(IntPtr p)
        {
            GCHandle h = (GCHandle)p;
            authorizer_hook_info hi = h.Target as authorizer_hook_info;
            return hi;
        }

        internal int call(int action_code, string param0, string param1, string dbName, string inner_most_trigger_or_view)
        {
            return _func(_user_data, action_code, param0, param1, dbName, inner_most_trigger_or_view);
        }
    }

    internal class authorizer_hook_handle : hook_handle
    {
        internal authorizer_hook_handle(delegate_authorizer func, object v)
			: base(new authorizer_hook_info(func, v))
        {
        }
    }

}

