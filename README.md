# Workshop walkthrough

## Introduction

This workshop is designed to help developers with Relational Database backgrounds
learn about Document Database data considerations.

We'll take a very simple relational model and walk through several refactorings
as we develop a Document model.

## Prerequisites

We are using Azure DocumentDB as we work through examples. You should
be able to apply these ideas to any document-based database, such as MongoDB.

Assuming you'll be working with DocumentDB:

### Creating a database to work with

 - Create a new DocumentDB account via the portal
 - Find your database URI and keys (in account settings)
 - create a database with a single collection (lowest tier, S1, is fine). Why a single collection? We'll get to that in a bit...

### How to query

 - Via the Azure portal (portal.azure.com), the DocumentDB panel has a query window
 - DocDBStudio (Windows-only, downloadable from [here](https://github.com/mingaliu/DocumentDBStudio))
 - code (we have simple query-runner command-line apps written in
 both node and .net that you may use during the workshop)

### How to measure queries

Each DocumentDB query has a related Request Unit (RU) cost.
A query's cost is returned along with the query result. This
is displayed in the portal and DocDBStudio. Both the node and .net
examples also show how to extract and display the RU cost.

# The Relational Model

For the workshop, we'll tackle a Task database. And to make it a bit more interesting,
it has a bunch of tables and normalization:

 Table | description
 :------|:------------
 Person | Registered people
 Category | Task categories
 Status | Task status
 Task | The actual task itself, with foreign keys to Person and Status
 TaskNotes | Optional notes for a task (A person may enter any number of notes)
 TaskCategories | categories for each task (a task may have multiple categories)
 TaskAssistants | Optionally, people assigned to help with tasks (but not task owner)

### Relational queries

To start, it's helpful to see the types of queries are needed to render task details.


#### Getting tasks

Here, we'll get all tasks for a given person, along with description, categories
 and status.

```
select p.username, t.description, c.CategoryName, s.description from Person p
inner join task t on p.Id=t.PersonId
inner join TaskCategories tc on tc.TaskId = t.Id
inner join Category c on tc.CategoryId = c.Id
inner join Status s on t.TaskStatusId = s.Id
where p.username="David"
```

### Getting the list of people with tasks assigned

First: A list of primary task owners

```
select distinct p.username from Person p
inner join Task t on t.PersonId = p.Id
```
Now, find people who are tagged as assistants

```
select distinct p.username as "Assistant",t.description as "Assisting with" from Person p
inner join TaskAssistants ta on ta.PersonId = p.Id
inner join Task t on ta.TaskId = t.Id
```
# Moving to a Document database schema

Ok, so we have many tables comprising our Person- and Task- database. Let's now 
think in terms of Document models. We'll take things a step at a time, looking at
various options and pros/cons. The hope is that we"ll find a workable model.

## A few notes about DocumentDB

### DocumentDB and ID's

Each DocumentDB document must have a unique id property (specifically, `id`). When creating documents via
the portal, you'll need to specificy this id. When using one of the SDK's, a guid-based id will be created
for you if you omit it.

The `id` property must be a striing.

### DocumentDB and collections

When arriving from a relational world, it's easy to imagine a document collection being the equivalent
of a SQL table: A grouping of similar content (e.g. a Person collection or a Task collection).
However, that analogy isn't really accurate, as a collection may contain *any* type of data,
whether homogeneous or heterogeneous. Even content of same type could have different property
subsets. This is an inherent advantage of document databases, since there's no enforced
per-document schema.

When executing queries, the typical boundary is the collection. If, for example, you split your
Person and Task data across multiple collections, and you want to retrieve a person and all of their
tasks, you'll need to execute multiple queries to retrieve this data.

One more consideration: DocumentDB scales on a per-collection basis. Each collection has storage and
performance characteristics, and a related cost. To reduce cost (especially in this learning exercise),
it makes fiscal sense to store all data in a single collection.

If differentiation of types becomes an issue (e.g. Task and Person each have a `Name` property, and you
only want to query names of Persons), consider adding a `Type` property to each document, which allows
you to filter via `WHERE` clause.

For this workshop, assume all data is stored in a single collection.


## Our two most important entities: Person and Task

Ok, now that we have the prerequisites out of the way, let's dig in to the modeling.
This data model is all about tasks, and the people who are assigned to them. If you
think about this in terms of JSON, there are two obvious first-cut approaches:

### Person+Task approach 1: Separate Person and Task documents

This is similar to a relational junction table:
```
{
	"type": "Person",
	"id": "Person1"
}
{
	"type": "Task",
	"id": "Task2"
}
{
	"type": "TaskAssignment",
	"id": "...",
	"taskId": "Task2",
	"personId": "Person1"
}
```
This approach keeps Persons and Tasks completely separate. However, this now
requires up to *three* reads / writes per operation. And it really doesn't buy
much in terms of usefulness.

### Person+Task approach 2: People containing task lists

Imagine each Person document contains an array of tasks:

```
{
	"type": "Person",
	"id": "Person1",
	"username": "David",
	"tasks": [
		{
			"taskId": "Task1",
			"description": "Pack for CloudDevelop",
			"status": "In progress"
		},
		{
			"taskId": "Task2",
			"description": "Finalize walkthrough doc",
			"status": "Complete"
		}
	]
}
```
This seems reasonable, except for a few challenges:
 - The task list is an unbounded array
 - Task queries become a bit unwieldy, as they always involve searching through
 Person data.

Regarding the unbounded array: We could reduce risk by storing an array of Task Id's
within a Person document, and moving Tasks to separate documents:
```
{
	"type": "Person",
	"id": "Person1",
	"username": "David",
	"tasks": [ "Task1", "Task2" ]
}
```
This is much more efficient in terms of Person documents storage. However,
it would require an additional read to fetch Task info (stored in separate
documents). Same for writes: Each
new task would generate an insert (Task) and an update (Person).


### Person+Task approach 3: Task lists containing Persons

Flipping the model around, we could have the following, where Task is the top-level
(containing) document, which has a reference to the assigned person. Note: In the
 event we want to assign *helpers* to a task, this approach accomodates this
fairly easily, with the addition of an `AssistantId` property, which may be either
a single value or an array (yes, this is potentially unbounded, but for tasks, not
a realistic concern).
```
{
	"type": "Task",
	"id": "Task2",
	"personId": "Person1",
	"assistants": [ "Person2" ],
	"description": "Finalize walkthrough doc"
}
```
This approach seems a bit more reasonable, but it requires another transaction if you
need to print out the Person's name, for instance. In this case, it might be worth
denormalizing a bit:
```
{
	"type": "Task",
	"id": "Task2",
	"personId": "Person1",
	"username": "David",
	"assistants": [ {"personId": "Person2", "username": "Ryan"} ]
	...
}
```

This approach (putting the person's ID in the task)
is equivalent to the PersonId foreign key in the relational Task table.


### Which to choose?

Looking at all three approaches, **approach #3** (Tasks with references to assigned person(s)) in general seems to fit our needs
the best. If there's ever a need to have speedier access to task details for a
given Person, #3 could be combined with approach #2, storing a Task Id array
within a Person document. For now, we'll stick with approach #3.


## Task categories

Thinking about our Task list, we probably want each task to have one or more assignable categories.

With our relational model, we kept a normalized list of categories in a Category table:

Id | CategoryName
:--|:-----------
1  | Chores
2  | School
3  | Work

Since each task may have multiple categories, we would likely have a TaskCategories table:

TaskId | CategoryId
:------|:----------
1 | 3
2 | 3
2 | 5

Let's think about how we'd model this with documents.

### Task Categories: Approach 1 - TaskCategories

In a document model, we could have something similar to our relational model:
 - Task document, with no categories
 - A TaskCategories document, with a Task ID and array of Category id's

Note: With the relational tables, we have one row per task category. We could
certainly do that with Documents, but since we have the ability to contain
an array of IDs within a document, it make sense to take advantage of arrays.

We"d then have something like this:

```
{
	"type": "Category",
	"id": "Category3",
	"description": "Work"
}
{
	"type": "Category",
	"id": "Category5",
	"description": "Indoors"
}
{
	"type": "Task",
	"id": "Task2",
	"description": "Finalize walkthrough doc"
}
{
	"type": "TaskCategories",
	"id": "...",
	"taskId": "Task2"
	"categoryIds": [ "Category3", "Category5" ]
}
```

However:
 - This would require multiple lookups, to retrieve task detail and category detail
 - To find all tasks within a given category, you"d need to search each array
   of TaskCategories for a given category ID (and then query each Task by Id to get
   task details).

### Task Categories: Approach 2 - Task with category IDs

As an alternative, we can simply store the array of categories directly in the Task document.

```
{
	"type": "Task",
	"id": "Task2",
	"description": "Finalize walkthrough doc",
	"categories": [ "Category3", "Category5" ]
}
```
Approach #2 still requires searching each task's array to find all tasks for a given category,
but there'd be no need for additional queries to retrieve Task details.

Optionally, we could denormalize data to optimize our read query:
 - Task document, with array of Category ID + Category name

```
{
	"type": "Task",
	"id": "Task2",
	"description": "Finalize walkthrough doc",
	categories: [
		{ "categoryId": "Category3", categoryName: "Work" },
		{ "categoryId": "Category5", categoryName: "Indoors" }
	]
}
```
Denormalization has a slight downside: If a category name changed, you might need to
find all tasks with that category, and update the task document. If you
choose to keep categories as is, and just make them *disabled* in lieu of a
newer category, then you wouldn't have the data update issue.


## Task notes

Notes are interesting, in that they are *unbounded*. That is, there's no limit 
to the number of notes that a Task may have. Liken this to comments on a blog post:
Unless there's a specific reason to limit this in the app, the database must
accomodate the unbounded condition, with unlimited notes.

The challenge is that Documents are capped in size. With DocumentDB, that size is 512K.
While it's not very likely you'll exceed this limit while storing a Task, it's not difficult
to imagine this happening if you kept copious, detailed notes for each of your tasks.

### Task notes: Approach 1 - Array within Task

Here's what our *unbounded* Task+Notes document might look like:

```
{
	"type": "Task",
	"id": "Task2",
	"description": "Finalize walkthrough doc",
	"notes": [
		{ "noteId": "Note1", "note": "Reviewed relational model w/Ryan"},
		{ "noteId": "Note2", "note": "Reviewed document model w/Ryan"}
	]
}
```
This is very easy to query. However, if notes can be significant in size, this
could end up pushing the limits of the maximum Document size (not too likely, but still
possible).

### Task notes: Approach 2 - Discrete note documents

To avoid the unbounded-array issue, Notes may be stored in separate Documents:
```
{
	"type": "Task",
	"id": "Task2",
	"description": "Finalize walkthrough doc"
}
{
	"type": "Note",
	"noteId": "Note1",
	"taskId": "Task2",
	"note": "Reviewed relational model w/Ryan"
}
```
We now have two queries if we want a task plus notes. However, if notes aren't
often viewed immediately (e.g. you merely list the Task names, and then show
notes only when a task's details are shown), this might not really impact
performance.

### Note Id's within Task

If you needed to quickly discover how many notes a particular task had (if any), you
could optionally store an array of Note Id's into the Task document.

```
{
	"type": "Task",
	"id": "Task2",
	"description": "Finalize walkthrough doc",
	"notes": [ "Note1", "Note5", "Note7" ]
}
```

### Note highlights within Task

Just like online merchants display one or two relevant product reviews on a product summary
page, you may want to show the most recent note (or at least part of it). So... consider
this bit of denormalization, storing something about the most recent note (or array of most recent notes):

```
{
	"type": "Task",
	"id": "Task2",
	"description": "Finalize walkthrough doc",
	"notes": [ "Note1", "Note5", "Note7" ],
    mostRecentNotes: [
    	{ noteId: "Note1", timestamp: "", summary: "I reviewd..."}
    ]
}
```


# Putting it all together - our Document model

So... we have several options for our final model, each with various tradeoffs around:
 - Embed vs reference (and volatility)
 - Number of reads / writes required
 - Unbounded vs bounded
 - Denormalization

There's really no single correct way to model this data, but here's a strawman example,
based on the following decisions:
 - Task-first approach. Each task references the person(s) working on the task.
 - Along with Person id in tasks, we'll denormalize and store username for faster retrieval
 - Task categories referenced as an array of category ID's within a Task
 - Task notes stored as separate documents, with note id arrays stored within a task,
 along with a "recent note" text teaser.

Again, this is a strawman, and you might design something completely different. Or, maybe
you start with the task-first approach, and then find out, through telemetry,
 people are using your app in a Person-first way and you need to remodel your data.

Ok let's put this all together:

```
{
	"type": "Category",
	"id": "Category3",
	"description": "Work"
}
{
	"type": "Category",
	"id": "Category5",
	"description": "Indoors"
}
{
	"type": "Person",
	"id": "Person1",
	"username": "David",
	"tasks": [ "Task1", "Task2" ]
}
{
	"type": "Person",
	"id": "Person2",
	"username": "Ryan",
	"assistWithTasks": [ "Task2" ]
}
{
	"type": "Task",
	"id": "Task2",
	"person": { "personId": "Person1", "username": "David" },
	"assistants": [ { "personId": "Person2", "username": "Ryan" } ],
	"description": "Finalize walkthrough doc",
	"categories": [
		{ "categoryId": "Category3", "description": "Work"},
		{ "categoryId": "Category5", "description": "Indoors"}
	],
	"notes": [ "Note1", "Note2" ],
    "mostRecentNotes": [
    	{ "noteId": "Note2", "summary": "I reviewde relational model w/Ryan..."}
    ]
}
{
	"type": "Note",
	"id": "Note2",
	"person": { "personId": "Person1", "username": "David"},
	"note": "I reviewed relational model w/Ryan and blah blah foo bar something really long goes in to this note. Yay."
}
```

### Query examples: Strawman model

#### Getting tasks

Get all tasks for a given person

```
SELECT t.description, t.person.username, a.username as Assistants
FROM t
JOIN a in t.assistants
WHERE t.type="Task"
AND t.person.username="David"
```

#### Getting the list of people with tasks assigned

First: A list of primary task owners

```
SELECT p.username as TaskOwner from p
WHERE p.type="Person"
and is_defined(p.tasks)
```

Now, find people who are tagged as assistants

```
SELECT p.username as Assistant from p
WHERE p.type="Person"
and is_defined(p.assistWithTasks)
```

Retrieve the category list for a given task:

```
select c.description as Category from t 
join c in t.categories
where t.type="Task" and t.id="Task2"
```