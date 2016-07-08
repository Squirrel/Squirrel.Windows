﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;

namespace Squirrel.Update
{
    public static class CopStache
    {
        public static string Render(string template, Dictionary<string, string> identifiers)
        {
            var buf = new StringBuilder();

            foreach (var line in template.Split('\n')) {
                identifiers["RandomGuid"] = (Guid.NewGuid()).ToString();

                foreach (var key in identifiers.Keys) {
                    buf.Replace("{{" + key + "}}", SecurityElement.Escape(identifiers[key]));
                }

                buf.AppendLine(line);
            }

            return buf.ToString();
        }
    }
}
