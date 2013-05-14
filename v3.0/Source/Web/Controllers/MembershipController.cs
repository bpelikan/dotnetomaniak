using System.Net.Configuration;
using Kigg.Core.DomainObjects;
using Kigg.Core.Repository;
using Kigg.Infrastructure.LinqToSql.Repository;
using Kigg.LinqToSql.DomainObjects;
using Kigg.LinqToSql.Repository;

namespace Kigg.Web
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Web;
    using System.Web.Mvc;

    using DotNetOpenAuth.OpenId;
    using DotNetOpenAuth.OpenId.Extensions.SimpleRegistration;
    using DotNetOpenAuth.OpenId.RelyingParty;
    using DotNetOpenAuth.Messaging;

    using DomainObjects;
    using Infrastructure;
    using Service;

    public class MembershipController : BaseController
    {
        private const int MinimumLength = 4;

        private static readonly Regex UserNameExpression = new Regex(@"^([a-zA-Z])[a-zA-Z_-]*[\w_-]*[\S]$|^([a-zA-Z])[0-9_-]*[\S]$|^[a-zA-Z]*[\S]$", RegexOptions.Singleline | RegexOptions.Compiled);
        private readonly IDomainObjectFactory _factory;
        private readonly IEventAggregator _eventAggregator;
        private readonly IEmailSender _emailSender;
        private readonly IBlockedIPCollection _blockedIPList;
        private readonly IRecommendationRepository _recommendationRepository;

        public MembershipController(IDomainObjectFactory factory, IEventAggregator eventAggregator, IEmailSender emailSender, IBlockedIPCollection blockedIPList)
        {
            Check.Argument.IsNotNull(factory, "factory");
            Check.Argument.IsNotNull(eventAggregator, "eventAggregator");
            Check.Argument.IsNotNull(emailSender, "emailSender");
            Check.Argument.IsNotNull(blockedIPList, "blockedIPList");

            _factory = factory;
            _eventAggregator = eventAggregator;
            _emailSender = emailSender;
            _blockedIPList = blockedIPList;
        }

        private bool OpenIdRememberMe
        {
            get
            {
                bool rememberMe = false;

                HttpCookie cookie = Request.Cookies["oidr"];

                if (cookie != null)
                {
                    if (!bool.TryParse(cookie.Value, out rememberMe))
                    {
                        rememberMe = false;
                    }

                    cookie = Response.Cookies["oidr"];

                    if (cookie != null)
                    {
                        cookie.Expire();
                    }
                }

                return rememberMe;
            }
            set
            {
                if (value)
                {
                    var cookie = new HttpCookie("oidr")
                                            {
                                                Expires = DateTime.Now.AddMinutes(5),
                                                HttpOnly = false,
                                                Value = bool.TrueString
                                            };

                    HttpContext.Response.Cookies.Add(cookie);
                }
            }
        }

        private string ReturnUrl
        {
            get
            {
                string returnUrl = string.Empty;

                HttpCookie cookie = Request.Cookies["returnUrl"];

                if (cookie != null)
                {
                    returnUrl = cookie.Value;

                    cookie = Response.Cookies["returnUrl"];

                    if (cookie != null)
                    {
                        cookie.Expire();
                    }
                }

                return returnUrl;
            }
            set
            {
                if (!string.IsNullOrEmpty(value))
                {
                    var cookie = new HttpCookie("returnUrl")
                                            {
                                                Expires = DateTime.Now.AddMinutes(5),
                                                HttpOnly = false,
                                                Value = value
                                            };

                    HttpContext.Response.Cookies.Add(cookie);
                }
            }
        }

        public IOpenIdRelyingParty OpenIdRelyingParty
        {
            get;
            set;
        }

        [Compress]
        public ActionResult OpenId(string identifier, bool? rememberMe)
        {
            string errorMessage = null;

            string url = string.Concat(Settings.RootUrl, Url.Content("~/xrds.axd"));
            HttpContext.Response.Headers.Add("X-XRDS-Location", url);

            try
            {
                if (OpenIdRelyingParty.Response == null)
                {
                    if (!string.IsNullOrEmpty(identifier))
                    {
                        Identifier id;

                        if (Identifier.TryParse(identifier, out id))
                        {
                            var realm = new Realm(new Uri(string.Concat(Settings.RootUrl, Url.RouteUrl("OpenId"))));

                            IAuthenticationRequest request = OpenIdRelyingParty.CreateRequest(identifier, realm);

                            var fetch = new ClaimsRequest
                                            {
                                                Email = DemandLevel.Require
                                            };

                            request.AddExtension(fetch);

                            OpenIdRememberMe = rememberMe ?? false;
                            ReturnUrl = (HttpContext.Request.UrlReferrer != null) ? HttpContext.Request.UrlReferrer.ToString() : string.Empty;

                            return request.RedirectingResponse.AsActionResult(); //.RedirectToProvider();
                        }
                    }

                    return new EmptyResult();
                }

                if (OpenIdRelyingParty.Response.Status == AuthenticationStatus.Authenticated)
                {
                    string userName = OpenIdRelyingParty.Response.ClaimedIdentifier;

                    using (IUnitOfWork unitOfWork = UnitOfWork.Begin())
                    {
                        IUser user = UserRepository.FindByUserName(userName);

                        if ((user != null) && user.IsLockedOut)
                        {
                            errorMessage = "Twoje konto jest obecnie zablokowane. Skontaktuj si� z nami w tej sprawie.";
                        }
                        else
                        {
                            var fetch = OpenIdRelyingParty.Response.GetExtension<ClaimsResponse>();

                            // Some of the Provider does not return Email
                            // Such as Yahoo, Blogger, Bloglines etc, in that case we will assign a default
                            // email
                            string email = ((fetch == null) || string.IsNullOrEmpty(fetch.Email)) ? Settings.DefaultEmailOfOpenIdUser : fetch.Email;

                            if (user == null)
                            {
                                user = _factory.CreateUser(userName, email, null);
                                UserRepository.Add(user);
                                _eventAggregator.GetEvent<UserActivateEvent>().Publish(new UserActivateEventArgs(user));
                            }
                            else
                            {
                                //Sync the email from OpenID provider.
                                if ((string.Compare(email, user.Email, StringComparison.OrdinalIgnoreCase) != 0) &&
                                    (string.Compare(email, Settings.DefaultEmailOfOpenIdUser, StringComparison.OrdinalIgnoreCase) != 0))
                                {
                                    user.ChangeEmail(email);
                                }
                            }

                            user.LastActivityAt = SystemTime.Now();

                            unitOfWork.Commit();
                            FormsAuthentication.SetAuthCookie(userName, OpenIdRememberMe);
                        }
                    }
                }
                else if ((OpenIdRelyingParty.Response.Status == AuthenticationStatus.Failed) || (OpenIdRelyingParty.Response.Status == AuthenticationStatus.Canceled))
                {
                    if (OpenIdRelyingParty.Response.Exception!= null)
                    {
                        errorMessage = OpenIdRelyingParty.Response.Exception.Message;
                    }
                    if (String.IsNullOrEmpty(errorMessage))
                    {
                        errorMessage = "Nie uda�o si� zalogowa� przez wybranego dostawc� OpenID.";
                    }
                }
            }
            catch (Exception oid)
            {
                errorMessage = oid.Message;
            }

            if (!string.IsNullOrEmpty(errorMessage))
            {
                GenerateMessageCookie(errorMessage, true);
            }

            string returnUrl = ReturnUrl;

            if (!string.IsNullOrEmpty(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToRoute("Published");
        }

        [AcceptVerbs(HttpVerbs.Post), Compress]
        public ActionResult Signup(string userName, string password, string email)
        {
            JsonViewData viewData = Validate<JsonViewData>(
                                                                new Validation(() => string.IsNullOrEmpty(userName.NullSafe()), "Nazwa u�ytkownika nie mo�e by� pusta."),
                                                                new Validation(() => userName.Trim().Length < MinimumLength, "Nazwa u�ytkownika nie mo�e by� kr�tsza ni� {0} znaki.".FormatWith(MinimumLength)),
                                                                new Validation(() => !UserNameExpression.IsMatch(userName), "Nazwa u�ytkownika mo�e zawiera� znaki i cyfry i zaczyna� si� liter�. Dopuszczalne znaki specjalne: -,_."),
                                                                new Validation(() => string.IsNullOrEmpty(password.NullSafe()), "Has�o nie mo�e by� puste."),
                                                                new Validation(() => password.Trim().Length < MinimumLength, "Has�o nie mo�e by� kr�tsze ni� {0} znak�w.".FormatWith(MinimumLength)),
                                                                new Validation(() => string.IsNullOrEmpty(email), "Adres e-mail nie mo�e by� pusty."),
                                                                new Validation(() => !email.NullSafe().IsEmail(), "Niepoprawny adres e-mail.")
                                                          );

            if (viewData == null)
            {
                try
                {
                    using (IUnitOfWork unitOfWork = UnitOfWork.Begin())
                    {
                        IUser user = _factory.CreateUser(userName.Trim(), email.Trim(), password.Trim());
                        UserRepository.Add(user);

                        unitOfWork.Commit();

                        string userId = user.Id.Shrink();

                        string url = string.Concat(Settings.RootUrl, Url.RouteUrl("Activate", new { id = userId }));

                        _emailSender.SendRegistrationInfo(email, userName, password, url);

                        Log.Info("User registered: {0}", user.UserName);

                        viewData = new JsonViewData { isSuccessful = true };
                    }
                }
                catch (ArgumentException argument)
                {
                    viewData = new JsonViewData { errorMessage = argument.Message };
                }
                catch (Exception e)
                {
                    Log.Exception(e);

                    viewData = new JsonViewData { errorMessage = FormatStrings.UnknownError.FormatWith("rejestracji") };
                }
            }

            return Json(viewData);
        }

        [AcceptVerbs(HttpVerbs.Post), Compress]
        public ActionResult Recomend(string recommendationLink, string recommendationTitle, string imageLink, string imageTitle)
        {
            JsonViewData viewData = Validate<JsonViewData>(
                                                                new Validation(() => string.IsNullOrEmpty(recommendationLink.NullSafe()), "Link rekomendacji nie mo�e by� pusty."),
                                                                new Validation(() => string.IsNullOrEmpty(recommendationTitle.NullSafe()), "Tytu� rekomendacji nie mo�e by� pusty."),
                                                                new Validation(() => string.IsNullOrEmpty(imageLink.NullSafe()), "Link obrazka nie mo�e by� pusty."),
                                                                new Validation(() => string.IsNullOrEmpty(imageTitle.NullSafe()), "Tytu� obrazka nie mo�e by� pusty.")
                                                          );

            if (viewData == null)
            {
                try
                {
                    using (IUnitOfWork unitOfWork = UnitOfWork.Begin())
                    {
                        IRecommendation recommendation = _factory.CreateRecommendation(recommendationLink.Trim(), recommendationTitle.Trim(), imageLink.Trim(), imageTitle.Trim());
                        _recommendationRepository.Add(recommendation);

                        unitOfWork.Commit();
                        
                        string recommendationId = recommendation.Id.Shrink();

                        string url = string.Concat(Settings.RootUrl, Url.RouteUrl("Activate", new { id = recommendationId }));

                        Log.Info("Recommendation registered: {0}", recommendation.RecommendationTitle);

                        viewData = new JsonViewData { isSuccessful = true };
                    }
                }
                catch (ArgumentException argument)
                {
                    viewData = new JsonViewData { errorMessage = argument.Message };
                }
                catch (Exception e)
                {
                    Log.Exception(e);

                    viewData = new JsonViewData { errorMessage = FormatStrings.UnknownError.FormatWith("") };
                }
            }

            return Json(viewData);
        }

        [AcceptVerbs(HttpVerbs.Post), Compress]
        public ActionResult Login(string userName, string password, bool? rememberMe)
        {
            JsonViewData viewData = Validate<JsonViewData>(
                                                                new Validation(() => string.IsNullOrEmpty(userName.NullSafe()), "Nazwa u�ytkownika nie mo�e by� pusta."),
                                                                new Validation(() => string.IsNullOrEmpty(password.NullSafe()), "Has�o nie mo�e by� puste.")
                                                          );

            if (viewData == null)
            {
                try
                {
                    using (IUnitOfWork unitOfWork = UnitOfWork.Begin())
                    {
                        IUser user = UserRepository.FindByUserName(userName.Trim());

                        if (user != null)
                        {
                            viewData = Validate<JsonViewData>(
                                                                new Validation(() => user.IsLockedOut, "Twoje konto jest aktualnie zablokowane. Skontaktuj si� z pomoc� aby rozwi�za� ten problem."),
                                                                new Validation(() => !user.IsActive, "Twoje konto nie zosta�o jeszcze aktywowane. Pos�� si� linkiem aktywacyjnym z wiadomo�ci rejestracyjnej aby aktywowa� konto."),
                                                                new Validation(() => user.IsOpenIDAccount(), "Podany login jest poprawny tylko z OpenID.")
                                                             );

                            if (viewData == null)
                            {
                                if (string.Compare(user.Password, password.Trim().Hash(), StringComparison.OrdinalIgnoreCase) == 0)
                                {
                                    user.LastActivityAt = SystemTime.Now();
                                    unitOfWork.Commit();

                                    FormsAuthentication.SetAuthCookie(userName, rememberMe ?? false);
                                    viewData = new JsonViewData { isSuccessful = true };

                                    Log.Info("U�ytkownik zalogowany: {0}", user.UserName);
                                }
                            }
                        }

                        if (viewData == null)
                        {
                            viewData = new JsonViewData { errorMessage = "Niepoprawne dane zalogowania." };
                        }
                    }
                }
                catch (Exception e)
                {
                    Log.Exception(e);
                    viewData = new JsonViewData { errorMessage = FormatStrings.UnknownError.FormatWith("logowania") };
                }
            }

            return Json(viewData);
        }

        [AcceptVerbs(HttpVerbs.Post), Compress]
        public ActionResult Logout()
        {
            JsonViewData viewData;

            if (IsCurrentUserAuthenticated)
            {
                try
                {
                    using (IUnitOfWork unitOfWork = UnitOfWork.Begin())
                    {
                        CurrentUser.LastActivityAt = SystemTime.Now();
                        unitOfWork.Commit();

                        FormsAuthentication.SignOut();
                        Log.Info("Wylogowano u�ytkownika: {0}", CurrentUserName);
                        viewData = new JsonViewData { isSuccessful = true  };
                    }
                }
                catch (Exception e)
                {
                    Log.Exception(e);

                    viewData = new JsonViewData { errorMessage = FormatStrings.UnknownError.FormatWith("wylogowywania") };
                }
            }
            else
            {
                viewData = new JsonViewData { errorMessage = "Nie jeste� zalogowany." };
            }

            return Json(viewData);
        }

        [AcceptVerbs(HttpVerbs.Post), Compress]
        public ActionResult ForgotPassword(string email)
        {
            JsonViewData viewData = Validate<JsonViewData>(
                                                                new Validation(() => string.IsNullOrEmpty(email.NullSafe()), "Pole e-mail nie mo�e by� puste."),
                                                                new Validation(() => !email.NullSafe().IsEmail(), "Niepoprawny adres e-mail.")
                                                          );

            if (viewData == null)
            {
                try
                {
                    using (IUnitOfWork unitOfWork = UnitOfWork.Begin())
                    {
                        IUser user = UserRepository.FindByEmail(email.Trim());

                        if (user == null)
                        {
                            viewData = new JsonViewData { errorMessage = "Nie znaleziono u�ytkownika z podanym adresem e-mail." };
                        }
                        else
                        {
                            try
                            {
                                string password = user.ResetPassword();

                                unitOfWork.Commit();

                                _emailSender.SendNewPassword(user.Email, user.UserName, password);

                                viewData = new JsonViewData { isSuccessful = true };

                                Log.Info("Wygenerowany nowe has�o dla: {0}", user.UserName);
                            }
                            catch (InvalidOperationException invalidOperation)
                            {
                                viewData = new JsonViewData { errorMessage = invalidOperation.Message };
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Log.Exception(e);

                    viewData = new JsonViewData { errorMessage = FormatStrings.UnknownError.FormatWith("resetowania has�a") };
                }
            }

            return Json(viewData);
        }

        [AcceptVerbs(HttpVerbs.Post), Compress]
        public ActionResult ChangePassword(string oldPassword, string newPassword, string confirmPassword)
        {
            JsonViewData viewData = Validate<JsonViewData>(
                                                                new Validation(() => string.IsNullOrEmpty(oldPassword.NullSafe()), "Stare has�o nie mo�e by� puste."),
                                                                new Validation(() => string.IsNullOrEmpty(newPassword.NullSafe()), "Nowe has�o nie mo�e by� puste."),
                                                                new Validation(() => newPassword.Trim().Length < MinimumLength, "Nowe has�o nie mo�e by� kr�tsz ni� {0} znak�w.".FormatWith(MinimumLength)),
                                                                new Validation(() => string.CompareOrdinal(newPassword.NullSafe(), confirmPassword.NullSafe()) != 0, "Nowe has�o i jego potwierdzenie s� r�ne."),
                                                                new Validation(() => !IsCurrentUserAuthenticated, "Nie jeste� zalogowany.")
                                                          );

            if (viewData == null)
            {
                try
                {
                    using (IUnitOfWork unitOfWork = UnitOfWork.Begin())
                    {
                        CurrentUser.ChangePassword(oldPassword.Trim(), newPassword.Trim());
                        unitOfWork.Commit();

                        viewData = new JsonViewData { isSuccessful = true };
                    }
                }
                catch (InvalidOperationException invalidOperation)
                {
                    viewData = new JsonViewData { errorMessage = invalidOperation.Message };
                }
                catch (Exception e)
                {
                    Log.Exception(e);

                    viewData = new JsonViewData { errorMessage = FormatStrings.UnknownError.FormatWith("zmiany has�a") };
                }
            }

            return Json(viewData);
        }

        [AcceptVerbs(HttpVerbs.Post), Compress]
        public ActionResult ChangeEmail(string email)
        {
            JsonViewData viewData = Validate<JsonViewData>(
                                                                new Validation(() => string.IsNullOrEmpty(email.NullSafe()), "Adres e-mail nie mo�e by� pusty."),
                                                                new Validation(() => !email.NullSafe().IsEmail(), "Niepoprawny adres e-mial."),
                                                                new Validation(() => !IsCurrentUserAuthenticated, "Nie jeste� zalogowany.")
                                                          );

            if (viewData == null)
            {
                try
                {
                    using (IUnitOfWork unitOfWork = UnitOfWork.Begin())
                    {
                        CurrentUser.ChangeEmail(email.Trim());
                        unitOfWork.Commit();

                        viewData = new JsonViewData { isSuccessful = true };
                    }
                }
                catch (InvalidOperationException invalidOperation)
                {
                    viewData = new JsonViewData { errorMessage = invalidOperation.Message };
                }
                catch (Exception e)
                {
                    Log.Exception(e);

                    viewData = new JsonViewData { errorMessage = FormatStrings.UnknownError.FormatWith("zmiany adresu email") };
                }
            }

            return Json(viewData);
        }

        [AcceptVerbs(HttpVerbs.Post), Compress]
        public ActionResult ChangeRole(string id, string role)
        {
            JsonViewData viewData = Validate<JsonViewData>(
                                                                new Validation(() => string.IsNullOrEmpty(id), "Identyfikator u�ytkownika nie mo�e by� pusty."),
                                                                new Validation(() => string.IsNullOrEmpty(role), "Rola nie mo�e by� pusta."),
                                                                new Validation(() => id.ToGuid().IsEmpty(), "Niepoprawny identyfikator u�ytkownika."),
                                                                new Validation(() => !IsCurrentUserAuthenticated, "Nie jeste� zalogowany."),
                                                                new Validation(() => !CurrentUser.IsAdministrator(), "Nie masz praw wo�a� t� metod�.")
                                                          );

            if (viewData == null)
            {
                try
                {
                    using (IUnitOfWork unitOfWork = UnitOfWork.Begin())
                    {
                        IUser user = UserRepository.FindById(id.ToGuid());

                        if (user == null)
                        {
                            viewData = new JsonViewData { errorMessage = "Podany u�ytkownik nie istnieje." };
                        }
                        else
                        {
                            user.Role = role.ToEnum(user.Role);
                            unitOfWork.Commit();

                            viewData = new JsonViewData { isSuccessful = true };
                        }
                    }
                }
                catch (Exception e)
                {
                    Log.Exception(e);

                    viewData = new JsonViewData { errorMessage = FormatStrings.UnknownError.FormatWith("zmiany roli") };
                }
            }

            return Json(viewData);
        }

        [AcceptVerbs(HttpVerbs.Post), Compress]
        public ActionResult Lock(string id)
        {
            return LockOrUnlock(id, false);
        }

        [AcceptVerbs(HttpVerbs.Post), Compress]
        public ActionResult Unlock(string id)
        {
            return LockOrUnlock(id, true);
        }

        [AcceptVerbs(HttpVerbs.Post), Compress]
        public ActionResult AllowIps(string id, ICollection<string> ipAddress)
        {
            JsonViewData viewData = Validate<JsonViewData>(
                                                                new Validation(() => string.IsNullOrEmpty(id), "Identyfikator u�ytkownika nie mo�e by� pusty."),
                                                                new Validation(() => id.ToGuid().IsEmpty(), "Niepoprawny identyfikator u�ytkownika."),
                                                                new Validation(() => !IsCurrentUserAuthenticated, "Nie jeste� zalogowany."),
                                                                new Validation(() => !CurrentUser.IsAdministrator(), "Nie masz praw wo�a� tej metody.")
                                                          );

            if (viewData == null)
            {
                try
                {
                    IUser user = UserRepository.FindById(id.ToGuid());

                    if (user == null)
                    {
                        viewData = new JsonViewData { errorMessage = "Podany u�ytkownik nie istnieje." };
                    }
                    else
                    {
                        ICollection<string> usedIpAddresses = UserRepository.FindIPAddresses(user.Id);
                        var blockedIps = new List<string>();

                        ipAddress = ipAddress ?? new List<string>();

                        if (!usedIpAddresses.IsNullOrEmpty())
                        {
                            foreach (string usedIpAddress in usedIpAddresses)
                            {
                                if (!ipAddress.Contains(usedIpAddress))
                                {
                                    blockedIps.Add(usedIpAddress);
                                }
                            }
                        }

                        if (blockedIps.Count > 0)
                        {
                            _blockedIPList.AddRange(blockedIps);
                        }

                        if (ipAddress.Count > 0)
                        {
                            _blockedIPList.RemoveRange(ipAddress);
                        }

                        viewData = new JsonViewData { isSuccessful = true };
                    }
                }
                catch (Exception e)
                {
                    Log.Exception(e);

                    viewData = new JsonViewData { errorMessage = FormatStrings.UnknownError.FormatWith("odblokowywania adres�w Ip u�ytkownika") };
                }
            }

            return Json(viewData);
        }

        [Compress]
        public ActionResult Activate(string id)
        {
            Guid userId = id.NullSafe().ToGuid();

            if (!userId.IsEmpty())
            {
                try
                {
                    using (IUnitOfWork unitOfWork = UnitOfWork.Begin())
                    {
                        IUser user = UserRepository.FindById(userId);

                        if ((user != null) && !user.IsActive)
                        {
                            user.IsActive = true;
                            user.LastActivityAt = SystemTime.Now();

                            _eventAggregator.GetEvent<UserActivateEvent>().Publish(new UserActivateEventArgs(user));

                            unitOfWork.Commit();

                            Log.Info("Aktywacja konta dla u�ytkownika: {0}", user.UserName);

                            GenerateMessageCookie("Twoje konto zosta�o aktywowane. Mo�esz si� teraz zalogowa�.", false);
                        }
                        else
                        {
                            GenerateMessageCookie("Niepoprawny klucz aktywacyjny.", true);
                        }
                    }
                }
                catch (Exception e)
                {
                    GenerateMessageCookie(FormatStrings.UnknownError.FormatWith("aktywacji twojego konta"), true);
                    Log.Exception(e);
                }
            }

            return RedirectToRoute("Published");
        }

        [Compress]
        public ActionResult List(int? page)
        {
            var viewData = CreateViewData<UserListViewData>();

            PagedResult<IUser> pagedResult = UserRepository.FindAll(PageCalculator.StartIndex(page, Settings.HtmlUserPerPage), Settings.HtmlUserPerPage);

            viewData.CurrentPage = page ?? 1;
            viewData.UserPerPage = Settings.HtmlUserPerPage;
            viewData.Users = pagedResult.Result;
            viewData.TotalUserCount = pagedResult.Total;

            viewData.Title = "{0} - U�ytkownicy".FormatWith(Settings.SiteTitle);
            viewData.Subtitle = "U�ytkownicy";
            viewData.NoUserExistMessage = "Brak u�ytkownik�w";

            return View(viewData);
        }
        
        public ActionResult AchievementsBy(string name)
        {
            if (string.IsNullOrEmpty(name))
                return RedirectToRoute("Users", new {page = 1});

            IUser user = UserRepository.FindById(name.ToGuid());

            PagedResult<IUserAchievement> achievements = user.Achievements;

            var viewData = CreateViewData<AchievementsViewData>();
            viewData.Count = 12;
            viewData.Achievements = achievements;

            return View("Achievements", viewData);
        }

        [Compress]
        public ActionResult Detail(string name, string tab, int? page)
        {
            if (string.IsNullOrEmpty(name))
            {
                return RedirectToRoute("Users", new { page = 1});
            }

            IUser user = null;
            Guid userId = name.NullSafe().ToGuid();

            if (!userId.IsEmpty())
            {
                user = UserRepository.FindById(userId);
            }

            if (user == null)
            {
                ThrowNotFound("U�ytkownik nie istnieje.");
            }

            UserDetailTab selectedTab = tab.ToEnum(UserDetailTab.Promoted);
            var viewData = CreateViewData<UserDetailViewData>();

            viewData.CurrentPage = page ?? 1;
            viewData.SelectedTab = selectedTab;
            viewData.TheUser = user;
            viewData.IPAddresses = new Dictionary<string, bool>();

            if ((user != null) && IsCurrentUserAuthenticated && CurrentUser.IsAdministrator())
            {
                foreach (string ipAddress in UserRepository.FindIPAddresses(user.Id))
                {
                    bool isAllowed = !_blockedIPList.Contains(ipAddress);

                    viewData.IPAddresses.Add(ipAddress, isAllowed);
                }
            }

            return View(viewData);
        }

        [ChildActionOnly]
        [ValidateInput(false)]
        public ActionResult Menu()
        {
            return View(new UserMenuViewData { IsUserAuthenticated = IsCurrentUserAuthenticated, CurrentUser = CurrentUser });
        }

        [ChildActionOnly]
        public ActionResult TopTabs()
        {
            DateTime maxTimestamp = SystemTime.Now();

            ICollection<UserWithScore> topLeaders = UserRepository.FindTop(Constants.ProductionDate, maxTimestamp, 0, Settings.TopUsers)
                                                                  .Result.Select(u => new UserWithScore { User = u, Score = u.CurrentScore })
                                                                  .ToList()
                                                                  .AsReadOnly();

            //DateTime minTimestamp = maxTimestamp.AddDays(-1);

            //ICollection<UserWithScore> topMovers = UserRepository.FindTop(minTimestamp, maxTimestamp, 0, Settings.TopUsers)
            //                                                     .Result.Select(u => new UserWithScore { User = u, Score = u.GetScoreBetween(minTimestamp, maxTimestamp) })
            //                                                     .ToList()
            //                                                     .AsReadOnly();

            var viewData = new TopUserTabsViewData
                               {
                                   TopLeaders = topLeaders,
                                   //TopMovers = topMovers
                               };

            return View(viewData);
        }

        private ActionResult LockOrUnlock(string id, bool unlock)
        {
            JsonViewData viewData = Validate<JsonViewData>(
                                                                new Validation(() => string.IsNullOrEmpty(id), "Identyfikator u�ytkownika nie mo�e by� pusty."),
                                                                new Validation(() => id.ToGuid().IsEmpty(), "Niepoprawny identyfikator u�ytkownika."),
                                                                new Validation(() => !IsCurrentUserAuthenticated, "Nie jeste� zalogowany."),
                                                                new Validation(() => !CurrentUser.IsAdministrator(), "Nie masz praw do wywo�ywania tej metody.")
                                                          );

            if (viewData == null)
            {
                try
                {
                    using (IUnitOfWork unitOfWork = UnitOfWork.Begin())
                    {
                        IUser user = UserRepository.FindById(id.ToGuid());

                        if (user == null)
                        {
                            viewData = new JsonViewData { errorMessage = "Podany u�ytkownik nie istnieje." };
                        }
                        else
                        {
                            if (unlock)
                            {
                                user.Unlock();
                            }
                            else
                            {
                                user.Lock();
                            }

                            unitOfWork.Commit();

                            viewData = new JsonViewData { isSuccessful = true };
                        }
                    }
                }
                catch (Exception e)
                {
                    Log.Exception(e);

                    viewData = new JsonViewData { errorMessage = FormatStrings.UnknownError.FormatWith("{0} u�ytkownika".FormatWith(unlock ? "odblokowywanie" : "blokowanie")) };
                }
            }

            return Json(viewData);
        }

        private void GenerateMessageCookie(string message, bool isError)
        {
            var cookie = new HttpCookie("notification")
                             {
                                 Expires = DateTime.Now.AddMinutes(5),
                                 HttpOnly = false
                             };

            cookie.Values.Add("msg", message);
            cookie.Values.Add("err", isError.ToString());

            HttpContext.Response.Cookies.Add(cookie);
        }

    }
}