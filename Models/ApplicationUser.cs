﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;

namespace AspTodo.Models
{
    // Add profile data for application users by adding properties to the ApplicationUser class
    public class ApplicationUser : IdentityUser
    {
        public virtual ICollection<TodoList> OwnedLists { get; set; }
        public virtual ICollection<Sharing> Sharings { get; set; }
        public virtual ICollection<Invitation> SentInvitations { get; set; }
        public virtual ICollection<Invitation> ReceivedInvitations { get; set; }

    }
}
