using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Web;
using System.Web.Helpers;
using System.Web.Hosting;
using System.Web.Mvc;
using System.Web.SessionState;
using Newtonsoft.Json;
using VeraDemoNet.DataAccess;
using VeraDemoNet.Models;

namespace VeraDemoNet.Controllers  
{  
    // https://www.c-sharpcorner.com/article/custom-authentication-with-asp-net-mvc/
    public class AccountController : AuthControllerBase
    {
        protected readonly log4net.ILog logger;

        private const string COOKIE_NAME = "UserDetails";

        public AccountController()
        {
            logger = log4net.LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);    
        }

        [HttpGet, ActionName("Login")]
        public ActionResult GetLogin()
        {
            LogoutUser();
            if (IsUserLoggedIn())
            {
                return GetLogOut();
            }


            var userDetailsCookie = Request.Cookies[COOKIE_NAME];

            if (userDetailsCookie == null || userDetailsCookie.Value.Length == 0)
            {
                logger.Info("No user cookie");
                Session["username"] = "";

                return View();
            }

            logger.Info("User details were remembered");

            var deserializedUser = JsonConvert.DeserializeObject<CustomSerializeModel>(userDetailsCookie.Value);

            logger.Info("User details were retrieved for user: " + deserializedUser.UserName);


            Session["username"] = deserializedUser.UserName;

            return RedirectToAction("Feed", "Blab");
        }

        [HttpPost, ActionName("Login")]
        public ActionResult PostLogin(LoginView loginViewModel)
        {
            try
            {
                if (ModelState.IsValid)
                {
                    var userDetails = LoginUser(loginViewModel.UserName, loginViewModel.Password);

                    
                    if (userDetails == null)
                    {
                        ModelState.AddModelError("CustomError", "Something Wrong : UserName or Password invalid ^_^ ");
                        return View(loginViewModel);
                    }

                    if (loginViewModel.RememberLogin)
                    {
                        var userModel = new CustomSerializeModel()
                        {
                            UserName = userDetails.UserName,
                            BlabName = userDetails.BlabName,
                            RealName = userDetails.RealName
                        };
                        
                        var faCookie =
                            new HttpCookie(COOKIE_NAME, JsonConvert.SerializeObject(userModel, Formatting.None))
                            {
                                Expires = DateTime.Now.AddDays(30)
                            };

                        Response.Cookies.Add(faCookie);
                    }
                    
                    return RedirectToAction("Feed", "Blab");

                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("CustomError", ex.Message);
            }

            return View(loginViewModel);

        }

        [HttpGet, ActionName("Logout")]
        public ActionResult GetLogOut()
        {
            var cookie = new HttpCookie("UserDetails", "")
            {
                Expires = DateTime.Now.AddYears(-1)
            };

            Response.Cookies.Add(cookie);

            LogoutUser();
            
            return Redirect(Url.Action("Login", "Account"));
        }

        [HttpGet, ActionName("Profile")]
        public ActionResult GetProfile()
        {
            logger.Info("Entering GetProfile");

            if (IsUserLoggedIn() == false)
            {
                return RedirectToLogin(HttpContext.Request.RawUrl);
            }

            var viewModel = new ProfileViewModel();

            var username = GetLoggedInUsername();

            using (var dbContext = new BlabberDB())
            {
                var connection = dbContext.Database.Connection;
                connection.Open();
                viewModel.Hecklers = RetrieveMyHecklers(connection, username);
                viewModel.Events = RetrieveMyEvents(connection, username);
                PopulateProfileViewModel(connection, username, viewModel);
            }

            return View(viewModel);
        }

        [HttpPost, ActionName("Profile")]
        public ActionResult PostProfile(string realName, string blabName, string userName, HttpPostedFileBase file)
        {
            logger.Info("Entering PostProfile");

            if (IsUserLoggedIn() == false)
            {
                return RedirectToLogin(HttpContext.Request.RawUrl);
            }

            var oldUsername = GetLoggedInUsername();
            var imageDir = HostingEnvironment.MapPath("~/Images/");
            string oldImage = null;
            using (var dbContext = new BlabberDB())
            {
                var user = dbContext.Users.FirstOrDefault(t => t.UserName == oldUsername);
                if (user == null)
                {
                    Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    return Json(new { message = "User cannot be found." });
                }
                oldImage = Path.Combine(imageDir,user.PictureName);
                var connection = dbContext.Database.Connection;
                connection.Open();

                var update = connection.CreateCommand();
                update.CommandText = "UPDATE users SET real_name=@realname, blab_name=@blabname WHERE username=@username;";
                update.Parameters.Add(new SqlParameter {ParameterName = "@realname", Value = realName});
                update.Parameters.Add(new SqlParameter {ParameterName = "@blabname", Value = blabName});
                update.Parameters.Add(new SqlParameter {ParameterName = "@username", Value = oldUsername});

                var result = update.ExecuteNonQuery();

                if (result == 0)
                {
                    Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    return Json(new { message = "An error occurred, please try again" });
                }
            }

            if (userName != oldUsername)
            {
                if (UsernameExists(userName))
                {
                    Response.StatusCode = (int) HttpStatusCode.Conflict;
                    return Json(new { message = "That username already exists. Please try another." });
                }
                if (!UpdateUsername(oldUsername, userName))
                {
                    Response.StatusCode = (int) HttpStatusCode.InternalServerError;
                    return Json(new {message = "An error occurred, please try again"});
                }
                Session["username"] = userName;
            }

            string newFilename = oldImage;
            // Update user profile image
            if (file != null &&  file.ContentLength > 0) 
            {
                // Get old image name, if any, to delete
                
                
                if (System.IO.File.Exists(oldImage))
                {
                    System.IO.File.Delete(oldImage);
                }
		
                var extension = Path.GetExtension(file.FileName).ToLower();
                newFilename = Path.Combine(imageDir, Guid.NewGuid().ToString("N"));
                newFilename += extension;

                logger.Info("Saving new profile image: " + newFilename);

                file.SaveAs(newFilename);
                using (var dbContext = new BlabberDB())
                {
                    var user = dbContext.Users.First(t => t.UserName == userName);
                    user.PictureName = Path.GetFileName(newFilename);
                    dbContext.SaveChanges();
                }

            }

            Response.StatusCode = (int)HttpStatusCode.OK;
            var msg = "Successfully changed values!";


            var newObject = new
            {
                values = new
                {
                    picturename = Path.GetFileName(newFilename),
                    username = userName.ToLower(),
                    realName = realName,
                    blabName = blabName
                },
                message = msg
            };
          
            return Json(newObject);
        }

        [HttpGet, ActionName("PasswordHint")]
        [AllowAnonymous]
        public ActionResult GetPasswordHint(string userName)
        {
            logger.Info("Entering password-hint with username: " + userName);
		
            if (string.IsNullOrEmpty(userName))
            {
                return Content("No username provided, please type in your username first");
            }

            try
            {
                using (var dbContext = new BlabberDB())
                {
                    var match = dbContext.Users.FirstOrDefault(x => x.UserName == userName);
                    if (match == null)
                    {
                        return Content("No password found for " + HttpUtility.HtmlEncode(userName));
                    }

                    if (match.PasswordHint == null)
                    {
                        return Content("Username '" + HttpUtility.HtmlEncode(userName) + "' has no password hint!");
                    }

                    var formatString = "Username '" + HttpUtility.HtmlEncode(userName) + "' has password: {0}";
                    return Content(string.Format(formatString, match.PasswordHint.Substring(0, 2) + new string('*', match.PasswordHint.Length - 2)));
                }
            }
            catch (Exception)
            {
                return Content("ERROR!");
            }
        }

        private bool UpdateUsername(string oldUsername, string newUsername)
        {
            // Enforce all lowercase usernames
            oldUsername = oldUsername.ToLower();
            newUsername = newUsername.ToLower();

            string[] sqlStrQueries =
            {
                "UPDATE users SET username=@newusername WHERE username=@oldusername",
                "UPDATE blabs SET blabber=@newusername WHERE blabber=@oldusername",
                "UPDATE comments SET blabber=@newusername WHERE blabber=@oldusername",
                "UPDATE listeners SET blabber=@newusername WHERE blabber=@oldusername",
                "UPDATE listeners SET listener=@newusername WHERE listener=@oldusername",
                "UPDATE users_history SET blabber=@newusername WHERE blabber=@oldusername"
            };

            using (var dbContext = new BlabberDB())
            {
                var connection = dbContext.Database.Connection;
                connection.Open();

                foreach (var sql in sqlStrQueries)
                {
                    using (var update = connection.CreateCommand())
                    {
                        logger.Info("Preparing the Prepared Statement: " + sql);
                        update.CommandText = sql;
                        update.Parameters.Add(new SqlParameter {ParameterName = "@oldusername", Value = oldUsername});
                        update.Parameters.Add(new SqlParameter {ParameterName = "@newusername", Value = newUsername});
                        update.ExecuteNonQuery();
                    }
                }
            }
            return true;
        }

        private bool UsernameExists(string username)
        {
            username = username.ToLower();

            // Check is the username already exists
            using (var dbContext = new BlabberDB())
            {
                var results = dbContext.Users.FirstOrDefault(x => x.UserName == username);
                return results != null;
            }
        }

        private void PopulateProfileViewModel(DbConnection connect, string username, ProfileViewModel viewModel)
        {
            string sqlMyProfile = "SELECT username, real_name, blab_name, is_admin, picture_name FROM users WHERE username = '" + username + "'";
            logger.Info(sqlMyProfile);

            using (var eventsCommand = connect.CreateCommand())
            {
                eventsCommand.CommandText = sqlMyProfile;
                using (var userProfile = eventsCommand.ExecuteReader())
                {
                    if (userProfile.Read())
                    {
                        viewModel.UserName = userProfile.GetString(0);
                        viewModel.RealName = userProfile.GetString(1);
                        viewModel.BlabName = userProfile.GetString(2);
                        viewModel.IsAdmin = userProfile.GetBoolean(3);
                        viewModel.Image = GetProfileImageName(userProfile.GetString(4));
                    }
                }
            }
        }
        
        [HttpGet, ActionName("DownloadProfileImage")]
	    public ActionResult DownloadProfileImage(string userName)
	    {
            if (IsUserLoggedIn() == false)
            {
                return RedirectToLogin(HttpContext.Request.RawUrl);
            }
            using (var db = new BlabberDB())
            {
                var user = db.Users.FirstOrDefault(u => u.UserName == userName);
                if (user == null)
                {
                    return HttpNotFound();
                }
                logger.Info("Entering downloadImage");

                var imagePath = Path.Combine(HostingEnvironment.MapPath("~/Images/"), user.PictureName);

                logger.Info("Fetching profile image: " + imagePath);

                return File(imagePath, System.Net.Mime.MediaTypeNames.Application.Octet);
            }
		    
        }

        [HttpGet, ActionName("register")]
        public ActionResult GetRegister()
        {
            logger.Info("Entering GetRegister");

            return View(new RegisterViewModel());
        }
        
        [HttpPost, ActionName("register")]
        public ActionResult PostRegister (string username)
        {
            logger.Info("PostRegister processRegister");
            var registerViewModel = new RegisterViewModel();

            Session["username"] = username;

            using (var dbContext = new BlabberDB())
            {

                registerViewModel.UserName = username;

                if (dbContext.Users.Any(t => t.UserName == username))
                {
                    registerViewModel.Error = "Username '" + username + "' already exists!";
                    return View(registerViewModel);
                }

                return View("RegisterFinish", registerViewModel);
            }
        }

        private string GetProfileImageName(string imageName)
        {
            var imagePath = HostingEnvironment.MapPath("~/Images/");
            var image =  Directory.EnumerateFiles(imagePath).FirstOrDefault(f => Path.GetFileName(f) == imageName);

            var filename = image == null ? "default_profile.png" : Path.GetFileName(image);
            
            return Url.Content("~/Images/" + filename);
        }

        private List<string> RetrieveMyEvents(DbConnection connect, string username)
        {
            // START BAD CODE
            var sqlMyEvents = "select event from users_history where blabber='" + 
                              username + "' ORDER BY eventid DESC; ";
            logger.Info(sqlMyEvents);
            
            var myEvents = new List<string>();
            using (var eventsCommand = connect.CreateCommand())
            {
                eventsCommand.CommandText = sqlMyEvents;
                using (var userHistoryResult = eventsCommand.ExecuteReader())
                {
                    while (userHistoryResult.Read())
                    {
                        myEvents.Add(userHistoryResult.GetString(0));
                    }
                }
            }

            // END BAD CODE

            return myEvents;
        }

        private List<Blabber> RetrieveMyHecklers(DbConnection connect, string username)
        {
            var hecklers = new List<Blabber>();
            var sqlMyHecklers = "SELECT users.username, users.blab_name, users.created_at, users.picture_name " +
                                "FROM users LEFT JOIN listeners ON users.username = listeners.listener " +
                                "WHERE listeners.blabber=@blabber AND listeners.status='Active'";

            using (var profile = connect.CreateCommand())
            {
                profile.CommandText = sqlMyHecklers;
                profile.Parameters.Add(new SqlParameter {ParameterName = "@blabber", Value = username});

                using (var myHecklersResults = profile.ExecuteReader())
                {
                    hecklers = new List<Blabber>();
                    while (myHecklersResults.Read())
                    {
                        var heckler = new Blabber
                        {
                            UserName = myHecklersResults.GetString(0),
                            BlabName = myHecklersResults.GetString(1),
                            CreatedDate = myHecklersResults.GetDateTime(2),
                            PictureName = myHecklersResults.GetString(3)
                        };
                        hecklers.Add(heckler);
                    }
                }
            }

            return hecklers;
        }

        [HttpGet, ActionName("RegisterFinish")]
        public ActionResult GetRegisterFinish()
        {
            logger.Info("Entering showRegisterFinish");

            return View();
        }

        [HttpPost, ActionName("RegisterFinish")]
        public ActionResult PostRegisterFinish([Bind(Include = "UserName,RealName,BlabName")]User user, string cpassword)
        {
            if (user.Password != cpassword)
            {
                logger.Info("Password and Confirm Password do not match");
                return View(new RegisterViewModel
                {
                    Error = "The Password and Confirm Password values do not match. Please try again.",
                    UserName = user.UserName,
                    RealName = user.RealName,
                    BlabName = user.BlabName,
                });
            }

            // Use the user class to get the hashed password.
            user.Password = Crypto.HashPassword(user.Password);
            user.CreatedAt = DateTime.Now;
            
            using (var dbContext = new BlabberDB())
            {
                dbContext.Users.Add(user);
                dbContext.SaveChanges();
            }

            var imageDir = HostingEnvironment.MapPath("~/Images/");
            try
            {
                System.IO.File.Copy(Path.Combine(imageDir, "default_profile.png"), Path.Combine(imageDir, user.UserName) + ".png");
            }
            catch (Exception ex)
            {

            }


            //EmailUser(userName);

            return RedirectToAction("Login", "Account", new LoginView {UserName = user.UserName});
        }
    }
}