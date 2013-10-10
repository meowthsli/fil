using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.Mvc;
using FILLang;

namespace MvcApplication1.Controllers
{
    public class HomeController : Controller
    {
        public ActionResult Index()
        {
            ViewBag.text = TEXT;
            return View();
        }

        public ActionResult Translate(string fil)
        {
            ViewBag.python = new PythonGenerator().Run(fil);
            return File(Encoding.UTF8.GetBytes(ViewBag.python), "text/python", "script.py");
        }

        private const string TEXT = @"x: value FROM some table.
y: first part OF x.
z: any value FROM [INNER value OF y] INTO nothing.";
    }
}
