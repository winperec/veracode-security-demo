using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Security.Cryptography;
using System.Text;
using System.Web.Helpers;

namespace VeraDemoNet.DataAccess
{
    public class User
    {
        public string UserName { get; set; }
        [Column("blab_name")] public string BlabName { get; set; }
        [Column("real_name")] public string RealName { get; set; }
        [Column("password")] public string Password { get; set; }
        [Column("password_hint")] public string PasswordHint { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("last_login")] public DateTime? LastLogin { get; set; }
        [Column("is_admin")] public bool IsAdmin { get; set; }
        [Column("picture_name")] public string PictureName { get; set; }
        public static User Create(string userName, string blabName, string realName, bool isAdmin = false)
        {
            var password = Crypto.HashPassword(userName);
            var createdAt = DateTime.Now;

            return new User(userName, password, createdAt, null, blabName, realName, isAdmin);
        }

        public User()
        {
            
        }

        public User(string userName, string password, DateTime createdAt, DateTime? lastLogin, string blabName, string realName, bool isAdmin) 
        {
            UserName = userName;
            Password = password;
            PasswordHint = password;
            CreatedAt = createdAt;
            LastLogin = lastLogin;
            BlabName = blabName;
            RealName = realName;
            IsAdmin = isAdmin;
            PictureName = $"{userName}.png";
        }

    }
}