﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WebMVC.Models
{
    public class Book
    {
        public int Id { get; set; }
        public string Isbn { get; set; }
        public string Title { get; set; }
        public DateTime? ReleaseDate { get; set; }
    }
}
