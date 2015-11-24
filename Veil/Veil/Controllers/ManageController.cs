﻿using System;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Data.Entity.Migrations;
using System.Data.Entity.Validation;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Transactions;
using System.Web;
using System.Web.Mvc;
using Microsoft.Ajax.Utilities;
using Microsoft.AspNet.Identity;
using Microsoft.Owin.Security;
using Stripe;
using Veil.DataAccess.Interfaces;
using Veil.DataModels;
using Veil.DataModels.Models;
using Veil.DataModels.Models.Identity;
using Veil.Helpers;
using Veil.Models;
using Veil.Services;
using Veil.Services.Interfaces;
using Member = Veil.DataModels.Models.Member;

namespace Veil.Controllers
{
    [Authorize(Roles = VeilRoles.MEMBER_ROLE)]
    public class ManageController : BaseController
    {
        public const string STRIPE_ISSUES_MODELSTATE_KEY = "StripeIssues";

        private readonly VeilSignInManager signInManager;
        private readonly VeilUserManager userManager;
        private readonly IVeilDataAccess db;
        private readonly IGuidUserIdGetter idGetter;
        private readonly IStripeService stripeService;

        public ManageController(VeilUserManager userManager, VeilSignInManager signInManager, IVeilDataAccess veilDataAccess, IGuidUserIdGetter idGetter, IStripeService stripeService)
        {
            this.userManager = userManager;
            this.signInManager = signInManager;
            db = veilDataAccess;
            this.idGetter = idGetter;
            this.stripeService = stripeService;
        }

        //
        // GET: /Manage/Index
        public async Task<ActionResult> Index(ManageMessageId? message)
        {
            switch (message)
            {
                case ManageMessageId.AddPhoneSuccess:
                    this.AddAlert(AlertType.Success, "Your phone number was added.");
                    break;
                case ManageMessageId.ChangePasswordSuccess:
                    this.AddAlert(AlertType.Success, "Your password has been changed.");
                    break;
                case ManageMessageId.SetTwoFactorSuccess:
                    this.AddAlert(AlertType.Success, "Your two-factor authentication provider has been set.");
                    break;
                case ManageMessageId.SetPasswordSuccess:
                    this.AddAlert(AlertType.Success, "Your password has been set.");
                    break;
                case ManageMessageId.RemoveLoginSuccess:
                    this.AddAlert(AlertType.Success,  "A login has been removed.");
                    break;
                case ManageMessageId.RemovePhoneSuccess:
                    this.AddAlert(AlertType.Success, "Your phone number was removed.");
                    break;
                case ManageMessageId.Error:
                    this.AddAlert(AlertType.Error, "An error has occurred.");
                    break;
            }

            var userId = GetUserId();

            string phoneNumber;

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);

            try
            {
                phoneNumber = await userManager.GetPhoneNumberAsync(userId);
            }
            catch (InvalidOperationException)
            {
                // If this happens, the user has been deleted in the database but still has a valid login cookie
                signInManager.AuthenticationManager.SignOut(DefaultAuthenticationTypes.ApplicationCookie);

                return RedirectToAction("Index", "Home");
            }
            
            var model = new IndexViewModel
            {
                HasPassword = HasPassword(),
                PhoneNumber = phoneNumber,
                TwoFactor = await userManager.GetTwoFactorEnabledAsync(userId),
                Logins = await userManager.GetLoginsAsync(userId),
                BrowserRemembered = await signInManager.AuthenticationManager.TwoFactorBrowserRememberedAsync(userId.ToString()),
                MemberFirstName = user.FirstName,
                MemberLastName = user.LastName,
                MemberEmail = user.Email,
                MemberVisibility = user.Member.WishListVisibility,
                ReceivePromotionalEmals = user.Member.ReceivePromotionalEmails,
                UserId = userId
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> UpdateProfile(IndexViewModel viewModel)
        {
            ManageMessageId? message = null;

            if (viewModel.UserId == null)
            {
                throw new HttpException(NotFound, "error");
            }

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == viewModel.UserId);

            if (user == null)
            {
                throw new HttpException(NotFound, "error");
            }

            user.FirstName = viewModel.MemberFirstName;
            user.LastName = viewModel.MemberLastName;
            user.PhoneNumber = viewModel.PhoneNumber;
            user.Member.ReceivePromotionalEmails = viewModel.ReceivePromotionalEmals;
            
            
            try
            {
                db.MarkAsModified(user);
                await db.SaveChangesAsync();
                this.AddAlert(AlertType.Success, "Updates made"); 
            }
            catch (Exception e)
            {
                this.AddAlert(AlertType.Error, e.ToString());
            }

            if (user.Email != viewModel.MemberEmail)
            {
                //TODO redirect to a view to warn user
                return RedirectToAction("ChangeEmail", new { NewEmail = viewModel.MemberEmail });
                
            }

            return RedirectToAction("Index", new { Message = message });
        }

        public async Task<ActionResult> ChangeEmail(string  newEmail)
        {

            if (String.IsNullOrWhiteSpace(newEmail))
            {
                throw new HttpException(NotFound, "No email was supplied");    
            }

            return View(newEmail);
        }


        [HttpPost, ActionName("ChangeEmail")]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> ChangeEmailConfirmed(string newEmail)
        {
            if (String.IsNullOrWhiteSpace(newEmail))
            {
                throw new HttpException(NotFound, "New email was null or empty");
            }

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == GetUserId());

            if (user == null)
            {
                throw new HttpException(NotFound, "No user with supplied Id found");
            }

            user.Email = newEmail;
            user.EmailConfirmed = false;

            using (TransactionScope newEmailScope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
            {
                try
                {
                    db.MarkAsModified(user);
                    await db.SaveChangesAsync();
                    await SendConfirmationEmail(user);
                    signInManager.AuthenticationManager.SignOut(DefaultAuthenticationTypes.ApplicationCookie);
                    newEmailScope.Complete();
                }
                catch (Exception)
                {
                    throw new HttpException(NotFound, "Error changing email address");
                }

                
            }
           
            return RedirectToAction("Index", "Home");

            //TODO send confirmation email to new address
            //TODO logout user'
        }


        private async Task SendConfirmationEmail(User user)
        {
            // For more information on how to enable account confirmation and password reset please visit http://go.microsoft.com/fwlink/?LinkID=320771
            // Send an email with this link
            string code = await userManager.GenerateEmailConfirmationTokenAsync(user.Id);
            var callbackUrl = Url.Action("ConfirmEmail", "Account",
                new
                {
                    userId = user.Id,
                    code = code
                },
                protocol: Request.Url.Scheme);

            await userManager.SendEmailAsync(user.Id,
                "Veil - Email change request",
                "<h1>Confirm this email to rejoin us at Veil</h1>" +
                "Please confirm your new email address by clicking <a href=\"" + callbackUrl + "\">here</a>");
        }

        //
        // POST: /Manage/RemoveLogin
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> RemoveLogin(string loginProvider, string providerKey)
        {
            ManageMessageId? message;
            var result = await userManager.RemoveLoginAsync(GetUserId(), new UserLoginInfo(loginProvider, providerKey));
            if (result.Succeeded)
            {
                var user = await userManager.FindByIdAsync(GetUserId());
                if (user != null)
                {
                    await signInManager.SignInAsync(user, isPersistent: false, rememberBrowser: false);
                }
                message = ManageMessageId.RemoveLoginSuccess;
            }
            else
            {
                message = ManageMessageId.Error;
            }
            return RedirectToAction("ManageLogins", new { Message = message });
        }

        //
        // POST: /Manage/EnableTwoFactorAuthentication
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> EnableTwoFactorAuthentication()
        {
            await userManager.SetTwoFactorEnabledAsync(GetUserId(), true);
            var user = await userManager.FindByIdAsync(GetUserId());
            if (user != null)
            {
                await signInManager.SignInAsync(user, isPersistent: false, rememberBrowser: false);
            }
            return RedirectToAction("Index", "Manage");
        }

        //
        // POST: /Manage/DisableTwoFactorAuthentication
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> DisableTwoFactorAuthentication()
        {
            await userManager.SetTwoFactorEnabledAsync(GetUserId(), false);
            var user = await userManager.FindByIdAsync(GetUserId());
            if (user != null)
            {
                await signInManager.SignInAsync(user, isPersistent: false, rememberBrowser: false);
            }
            return RedirectToAction("Index", "Manage");
        }

        //
        // GET: /Manage/ChangePassword
        public ActionResult ChangePassword()
        {
            return View();
        }

        //
        // POST: /Manage/ChangePassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            IdentityResult result = null; 

            try
            {
                result = await userManager.ChangePasswordAsync(GetUserId(), model.OldPassword, model.NewPassword);
            }
            catch (DbEntityValidationException ex)
            {
                this.AddAlert(AlertType.Error, ex.Message);

                return View(model);
            }

            if (result.Succeeded)
            {
                var user = await userManager.FindByIdAsync(GetUserId());

                if (user != null)
                {
                    await signInManager.SignInAsync(user, isPersistent: false, rememberBrowser: false);
                }

                return RedirectToAction("Index", new { Message = ManageMessageId.ChangePasswordSuccess });
            }

            AddErrors(result);

            return View(model);
        }

        //
        // GET: /Manage/SetPassword
        public ActionResult SetPassword()
        {
            return View();
        }

        //
        // POST: /Manage/SetPassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> SetPassword(SetPasswordViewModel model)
        {
            if (ModelState.IsValid)
            {
                var result = await userManager.AddPasswordAsync(GetUserId(), model.NewPassword);
                if (result.Succeeded)
                {
                    var user = await userManager.FindByIdAsync(GetUserId());
                    if (user != null)
                    {
                        await signInManager.SignInAsync(user, isPersistent: false, rememberBrowser: false);
                    }
                    return RedirectToAction("Index", new { Message = ManageMessageId.SetPasswordSuccess });
                }
                AddErrors(result);
            }

            // If we got this far, something failed, redisplay form
            return View(model);
        }

        //
        // GET: /Manage/ManageLogins
        public async Task<ActionResult> ManageLogins(ManageMessageId? message)
        {
            ViewBag.StatusMessage = message == ManageMessageId.RemoveLoginSuccess ? "The external login was removed." : message == ManageMessageId.Error ? "An error has occurred." : "";

            var user = await userManager.FindByIdAsync(GetUserId());
            if (user == null)
            {
                return View("Error");
            }
            var userLogins = await userManager.GetLoginsAsync(GetUserId());
            var otherLogins = signInManager.AuthenticationManager.GetExternalAuthenticationTypes().Where(auth => userLogins.All(ul => auth.AuthenticationType != ul.LoginProvider)).ToList();
            ViewBag.ShowRemoveButton = user.PasswordHash != null || userLogins.Count > 1;
            return View(new ManageLoginsViewModel
            {
                CurrentLogins = userLogins, OtherLogins = otherLogins
            });
        }

        /// <summary>
        ///     Displays a view for adding or removing addresses
        /// </summary>
        /// <returns>
        ///     The view.
        /// </returns>
        public async Task<ActionResult> ManageAddresses()
        {
            AddressViewModel model = new AddressViewModel();

            await model.SetupAddressesAndCountries(db, GetUserId());
            
            return View(model);
        }

        /// <summary>
        ///     Creates a new address for the member
        /// </summary>
        /// <param name="model">
        ///     <see cref="AddressViewModel"/> containing the address details
        /// </param>
        /// <returns>
        ///     Redirects back to ManageAddresses if successful
        ///     Redisplays the form if the information is invalid or a database error occurs
        /// </returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> CreateAddress(AddressViewModel model)
        {
            model.UpdatePostalCodeModelError(ModelState);

            if (!ModelState.IsValid)
            {
                this.AddAlert(AlertType.Error, "Some address information was invalid.");

                await model.SetupAddressesAndCountries(db, GetUserId());

                return View("ManageAddresses", model);
            }

            model.FormatPostalCode();

            MemberAddress newAddress = new MemberAddress
            {
                MemberId = GetUserId(),
                Address = model.MapToNewAddress(),
                ProvinceCode = model.ProvinceCode,
                CountryCode = model.CountryCode
            };

            db.MemberAddresses.Add(newAddress);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                // Get the exception which states if a foreign key constraint was violated
                SqlException innermostException = ex.GetBaseException() as SqlException;

                bool errorWasProvinceForeignKeyConstraint = false;

                if (innermostException != null)
                {
                    string exMessage = innermostException.Message;
                    
                    errorWasProvinceForeignKeyConstraint =
                        innermostException.Number == (int)SqlErrorNumbers.ConstraintViolation &&
                        exMessage.Contains(nameof(Province.ProvinceCode)) &&
                        exMessage.Contains(nameof(Province.CountryCode));
                }

                this.AddAlert(AlertType.Error,
                    errorWasProvinceForeignKeyConstraint
                        ? "The Province/State you selected isn't in the Country you selected."
                        : "An unknown error occured while adding the address.");

                await model.SetupAddressesAndCountries(db, GetUserId());
        
                return View("ManageAddresses", model);
            }

            this.AddAlert(AlertType.Success, "Successfully add a new address.");
            return RedirectToAction("ManageAddresses");
        }

        /// <summary>
        ///     Allows the member to edit an existing <see cref="MemberAddress"/>
        /// </summary>
        /// <param name="addressId">
        ///     The Id of the <see cref="MemberAddress"/> to be edited
        /// </param>
        /// <returns>
        ///     A view allowing the member to edit the address
        /// </returns>
        [HttpGet]
        public async Task<ActionResult> EditAddress(Guid? addressId)
        {
            if (addressId == null)
            {
                throw new HttpException(NotFound, "Address");
            }

            MemberAddress addressToEdit = await db.MemberAddresses.FindAsync(addressId);

            if (addressToEdit == null)
            {
                throw new HttpException(NotFound, "Address");
            }

            AddressViewModel model = new AddressViewModel
            {
                Id = addressToEdit.Id,
                StreetAddress = addressToEdit.Address.StreetAddress,
                POBoxNumber = addressToEdit.Address.POBoxNumber,
                City = addressToEdit.Address.City,
                PostalCode = addressToEdit.Address.PostalCode,
                ProvinceCode = addressToEdit.ProvinceCode,
                CountryCode = addressToEdit.CountryCode
            };

            await model.SetupCountries(db);

            return View(model);
        }

        /// <summary>
        ///     Creates a new address for the member
        /// </summary>
        /// <param name="id">
        ///     The Id of the address to edit
        /// </param>
        /// <param name="model">
        ///     <see cref="AddressViewModel"/> containing the address details
        /// </param>
        /// <returns>
        ///     Redirects back to ManageAddresses if successful
        ///     Redisplays the form if the information is invalid or a database error occurs
        /// </returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> EditAddress(Guid id, AddressViewModel model)
        {
            if (!ModelState.IsValid)
            {
                model.UpdatePostalCodeModelError(ModelState);

                this.AddAlert(AlertType.Error, "Some address information was invalid.");

                await model.SetupCountries(db);

                return View("ManageAddresses", model);
            }

            MemberAddress editedAddress = await db.MemberAddresses.FindAsync(id);

            if (editedAddress == null)
            {
                throw new HttpException(NotFound, "Address");
            }

            model.FormatPostalCode();

            editedAddress.Address = model.MapToNewAddress();
            editedAddress.ProvinceCode = model.ProvinceCode;
            editedAddress.CountryCode = model.CountryCode;

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                // Get the exception which states if a foreign key constraint was violated
                SqlException innermostException = ex.GetBaseException() as SqlException;

                bool errorWasProvinceForeignKeyConstraint = false;

                if (innermostException != null)
                {
                    string exMessage = innermostException.Message;

                    errorWasProvinceForeignKeyConstraint =
                        innermostException.Number == (int)SqlErrorNumbers.ConstraintViolation &&
                        exMessage.Contains(nameof(Province.ProvinceCode)) &&
                        exMessage.Contains(nameof(Province.CountryCode));
                }

                this.AddAlert(AlertType.Error,
                    errorWasProvinceForeignKeyConstraint
                        ? "The Province/State you selected isn't in the Country you selected."
                        : "An unknown error occured while adding the address.");

                await model.SetupCountries(db);

                return View(model);
            }

            this.AddAlert(AlertType.Success, "Successfully updated the address.");
            return RedirectToAction("ManageAddresses");
        }

        public async Task<ActionResult> ManageCreditCards()
        {
            BillingInfoViewModel model = new BillingInfoViewModel();

            await model.SetupCreditCardsAndCountries(db, GetUserId());

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = VeilRoles.MEMBER_ROLE)]
        public async Task<ActionResult> CreateCreditCard(string stripeCardToken)
        {
            if (string.IsNullOrWhiteSpace(stripeCardToken))
            {
                this.AddAlert(AlertType.Error, "Some credit card information is invalid.");

                return RedirectToAction("ManageCreditCards");
            }

            Member currentMember = await db.Members.FindAsync(GetUserId());

            if (currentMember == null)
            {
                // Note: There should be no way for this to happen.
                return new HttpStatusCodeResult(HttpStatusCode.InternalServerError);
            }

            MemberCreditCard newCard;

            try
            {
                newCard = stripeService.CreateCreditCard(currentMember, stripeCardToken);
            }
            catch (StripeException ex)
            {
                // Note: Stripe says their card_error messages are safe to display to the user
                if (ex.StripeError.Code == "card_error")
                {
                    this.AddAlert(AlertType.Error, ex.Message);
                    ModelState.AddModelError(STRIPE_ISSUES_MODELSTATE_KEY, ex.Message);
                }
                else
                {
                    this.AddAlert(AlertType.Error, "An error occured while talking to one of our backends. Sorry!");
                }

                return RedirectToAction("ManageCreditCards");
            }

            currentMember.CreditCards.Add(newCard);

            await db.SaveChangesAsync();

            this.AddAlert(AlertType.Success, "Successfully added a new Credit Card.");

            return RedirectToAction("ManageCreditCards");
        }

        //
        // POST: /Manage/LinkLogin
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult LinkLogin(string provider)
        {
            // Request a redirect to the external login provider to link a login for the current user
            return new AccountController.ChallengeResult(provider, Url.Action("LinkLoginCallback", "Manage"), GetUserId().ToString());
        }

        //
        // GET: /Manage/LinkLoginCallback
        public async Task<ActionResult> LinkLoginCallback()
        {
            var loginInfo = await signInManager.AuthenticationManager.GetExternalLoginInfoAsync(XsrfKey, GetUserId().ToString());
            if (loginInfo == null)
            {
                return RedirectToAction("ManageLogins", new { Message = ManageMessageId.Error });
            }
            var result = await userManager.AddLoginAsync(GetUserId(), loginInfo.Login);
            return result.Succeeded ? RedirectToAction("ManageLogins") : RedirectToAction("ManageLogins", new { Message = ManageMessageId.Error });
        }

        //
        // POST: /Manage/RememberBrowser
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult RememberBrowser()
        {
            var rememberBrowserIdentity = signInManager.AuthenticationManager.CreateTwoFactorRememberBrowserIdentity(GetUserId().ToString());
            signInManager.AuthenticationManager.SignIn(new AuthenticationProperties { IsPersistent = true }, rememberBrowserIdentity);

            return RedirectToAction("Index", "Manage");
        }

        //
        // POST: /Manage/ForgetBrowser
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ForgetBrowser()
        {
            signInManager.AuthenticationManager.SignOut(DefaultAuthenticationTypes.TwoFactorRememberBrowserCookie);

            return RedirectToAction("Index", "Manage");
        }

        #region Helpers
        // Used for XSRF protection when adding external logins
        private const string XsrfKey = "XsrfId";

        private void AddErrors(IdentityResult result)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError("", error);
            }
        }

        private Guid GetUserId()
        {
            return idGetter.GetUserId(User.Identity);
        }

        private bool HasPassword()
        {
            var user = userManager.FindById(idGetter.GetUserId(User.Identity));
            if (user != null)
            {
                return user.PasswordHash != null;
            }
            return false;
        }

        private bool HasPhoneNumber()
        {
            var user = userManager.FindById(idGetter.GetUserId(User.Identity));
            if (user != null)
            {
                return user.PhoneNumber != null;
            }
            return false;
        }

        public enum ManageMessageId
        {
            AddPhoneSuccess,
            ChangePasswordSuccess,
            SetTwoFactorSuccess,
            SetPasswordSuccess,
            RemoveLoginSuccess,
            RemovePhoneSuccess,
            Error
        }
        #endregion
    }
}