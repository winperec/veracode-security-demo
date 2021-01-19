using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using System.Web.Helpers;
using System.Web.Mvc;
using VeraDemoNet.DataAccess;

namespace VeraDemoNet.Controllers
{
    public abstract class AuthControllerBase : Controller
    {
        protected BasicUser LoginUser(string userName, string passWord)
        {
            if (string.IsNullOrEmpty(userName))
            {
                return null;
            }

            using (var dbContext = new BlabberDB())
            {
                
                var user = dbContext.Users.SingleOrDefault(t => t.UserName == userName);

                if (user != null)
                {
                    
                    if (Crypto.VerifyHashedPassword(user.Password, passWord))
                    {
                        Session["username"] = userName;
                        return new BasicUser(user.UserName, user.BlabName, user.RealName);
                    }
                }
            }

            return null;
        }

        protected string GetLoggedInUsername()
        {
            return Session["username"].ToString();
        }

        protected void LogoutUser()
        {
            Session.Clear();
            Session.Abandon();
            HttpContext.Response.Cookies.Add(new HttpCookie("ASP.NET_SessionId", ""));
        }

        protected bool IsUserLoggedIn()
        {
            return string.IsNullOrEmpty(Session["username"] as string) == false;

        }

        protected RedirectToRouteResult RedirectToLogin(string targetUrl)
        {
            return new RedirectToRouteResult(
                new System.Web.Routing.RouteValueDictionary
                (new
                {
                    controller = "Account",
                    action = "Login",
                    ReturnUrl = HttpContext.Request.RawUrl
                }));
        }

     
    }
}