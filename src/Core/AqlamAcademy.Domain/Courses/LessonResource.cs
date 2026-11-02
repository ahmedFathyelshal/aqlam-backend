using AqlamAcademy.Domain.Common;
using AqlamAcademy.Domain.Enums;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace AqlamAcademy.Domain.Courses
{
    public class LessonResource : BaseEntity
    {
        public Guid LessonId { get; set; }
        public CourseLesson Lesson { get; set; } = null!;

        [Required]
        [MaxLength(150)]
        public string DisplayName { get; set; } = string.Empty; // e.g., "Homework PDF"

        [Required]
        public string ResourceUrl { get; set; } = string.Empty; // S3 / Bunny.net storage link

        public ResourceType Type { get; set; }

        public bool IsDownloadable { get; set; } = true; // Security rule flag
    }

    
}
