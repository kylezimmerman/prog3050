/* Company.cs
 * Purpose: A class for game publisher and development companies
 * 
 * Revision History:
 *      Drew Matheson, 2015.10.02: Created
 */ 

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Veil.Models
{
    /// <summary>
    /// Represents a game company and the GameProducts they have been involved with
    /// </summary>
    public class Company
    {
        /// <summary>
        /// The Id for the company
        /// </summary>
        [Key]
        public Guid Id { get; set; }

        /// <summary>
        /// The company's full name
        /// </summary>
        [Required]
        public string Name { get; set; }

        /// <summary>
        /// Collection navigation property for the GameProducts the company has published or developed
        /// </summary>
        public virtual ICollection<GameProduct> GameProducts { get; set; }
    }
}