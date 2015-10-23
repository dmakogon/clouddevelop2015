# Workshop walkthrough

## Introduction

This workshop is designed to help developers with Relational Database backgrounds
learn about Document Database data considerations.

We'll take a very simple relational model and walk through several refactorings
as we develop a Document model.

## Interesting stuff to know

### How to set up database account

 - account creation
 - keys
 - create database
 - create collection

### How to query

 - Portal (if you have an Azure subscription)
 - DocDBStudio (Windows-only)
 - code (we'll show node, .net)

### How to measure queries
 - RU costs (in portal, in DocDBStudio, and in code)

### Discussion points
 - Collection per type?


# The Relational Model

For the workshop, we'll tackle a Task database. And to make it a bit more interesting,
it has a bunch of tables and normalization that goes beyond the basic task list:

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
where p.username='David'
```

### Getting the list of people with tasks assigned

First: A list of primary task owners

```
select distinct p.username from Person p
inner join Task t on t.PersonId = p.Id
```
Now, find people who are tagged as assistants

```
select distinct p.username as 'Assistant',t.description as 'Assisting with' from Person p
inner join TaskAssistants ta on ta.PersonId = p.Id
inner join Task t on ta.TaskId = t.Id
```
# Moving to a Document database schema

Ok, so we have many tables comprising our Person- and Task- database. Let's now 
think in terms of Document models. We'll take things a step at a time, looking at
various options and pros/cons. The hope is that we'll find a workable model.

## Our two most important entities: Person and Task

This data model is all about tasks, and the people who are assigned to them. If you
think about this in terms of JSON, there are two obvious first-cut approaches:

### Person+Task approach 1: Separate Person and Task documents

This is similar to a relational junction table:
```
{
	type: 'Person',
	personId: 1,
	...
}
{
	type: 'Task',
	taskId: 2,
	...
}
{
	Type: 'TaskAssignment',
	taskId: 2,
	personId: 1
}
```
This approach keeps Persons and Tasks completely separate. However, this now
requires up to *three* reads / writes per operation. And it really doesn't buy
much in terms of usefulness.

### Queries: approach 1

TBD
### Person+Task approach 2: People containing task lists

Imagine each Person document contains an array of tasks:

```
{
	type: 'Person',
	personId: 1,
	username: 'David',
	tasks: [
		{
			taskId: 1,
			description: 'Pack for CloudDevelop',
			status: 'In progress'
		},
		{
			taskId: 2,
			description: 'Finalize walkthrough doc',
			status: 'Complete'
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
	type: 'Person',
	personId: 1,
	username: 'David',
	tasks: [ 1, 2 ]
}
```
This is much more efficient in terms of Person documents storage. However,
it would require an additional read to fetch Task info (stored in separate
documents). Same for writes: Each
new task would generate an insert (Task) and an update (Person).

### Queries: approach 2

TBD

### Person+Task approach 3: Task lists containing Persons

Flipping the model around, we could have the following, where Task is the top-level
(containing) document, which has a reference to the assigned person. Note: In the
 event we want to assign *helpers* to a task, this approach accomodates this
fairly easily, with the addition of an `AssistantId` property, which may be either
a single value or an array (yes, this is potentially unbounded, but for tasks, not
a realistic concern).
```
{
	type: 'Task',
	taskId: 2,
	personId: 1,
	assistantIds: [ 2 ],
	description: 'Finalize walkthrough doc'
}
```
This approach seems a bit more reasonable, but it requires another transaction if you
need to print out the Person's name, for instance. In this case, it might be worth
denormalizing a bit:
```
{
	type: 'Task',
	taskId: '2',
	personId: 1,
	**personUsername: 'David'**,
	**assistants: [ {personId: 2, username: 'Ryan'} ],**
	...
}
```

This approach (putting the person's ID in the task)
is equivalent to the PersonId foreign key in the relational Task table.

### Queries: approach 3

TBD


### Which to choose?

Looking at all three approaches, approach #3 in general seems to fit our needs
the best. If there's ever a need to have speedier access to task details for a
given Person, #3 could be combined with approach #3, storing a Task Id array
within a Person document. For now, we'll stick with approach #3.


## Task categories

With our relational model, we kept a normalized list of categories in a Category table:

Id | CategoryName
:--|:-----------
1  | Chores
2  | School
3  | Work

Since each task may have multiple categories, we have a TaskCategories table:

TaskId | CategoryId
:------|:----------
1 | 3
2 | 3
2 | 5

### Task Categories: Approach 1 - TaskCategories

In a document model, we could have something similar to our relational model:
 - Task document, with no categories
 - A TaskCategories document, with a Task ID and array of Category id's

Note: With the relational tables, we have one row per task category. We could
certainly do that with Documents, but since we have the ability to contain
an array of IDs within a document, it seems to make sense to do so.

We'd then have something like this:

```
{
	type: 'Category',
	categoryId: 3,
	description: 'Work'
}
{
	type: 'Category',
	categoryId: 5,
	description: 'Indoors'
}
{
	type: 'TaskCategories'
	taskId: 2
	CategoryIds: [3,5]
}
```
However:
 - This would require two lookups.
 - To find all tasks within a given category, you'd need to search each array
   of TaskCategories for a given category ID (and then query each Task by Id to get
   task details).

### Task Categories: Approach 2 - Task with category IDs

As an alternative:
 - Task document, with array of Category ID's
 - Since there's a bounded number of categories, we don't have an issue storing them in
the main document.

```
{
	Type: 'Task',
	taskId: '2',
	description: 'Finalize walkthrough doc',
	categories: [ 3, 5 ]
}
```
Approach #2 still requires searching each task's array, but there'd be no need
for additional queries to retrieve Task details.

Optionally, we could denormalize data to optimize our read query:
 - Task document, with array of Category ID + Category name

```
{
	type: 'Task',
	taskId: '2',
	description: 'Finalize walkthrough doc',
	**categories: [**
		**{ categoryId: 3, categoryName: 'Work'},**
		**{ categoryId: 5, categoryName: 'Indoors'}**
	]
}
```
Denormalization has a slight downside: If a category name changed, you might need to
find all tasks with that category, and update the task document. If you
choose to keep categories as is, and just make them *disabled* in lieu of a
newer category, then you'd never have the data update issue.


### Queries: Task categories

#### Retrieve a person's tasks for a given category

TBD

#### Retrieve all tasks for a given category

TBD

#### Retrieve a person's task categories

TBD

## Task notes

Notes are interesting, in that they are *unbounded*. That is, there's no limit 
to the number of notes that a Task may have. Liken this to comments on a blog post:
Unless there's a specific reason to limit this in the app, the database must
accomodate the unbounded condition.

The challenge is that Documents are capped in size. With DocumentDB, that size is 512K.
While it's not very likely you'll exceed this limit while storing a Task, it's not difficult
to imagine this happening.

### Task notes: Approach 1 - Array within Task

Here's what our *unbounded* Task+Notes document might look like:

```
{
	type: 'Task',
	taskId: '2',
	description: 'Finalize walkthrough doc',
	notes: [
		{ timestamp: '', note: 'Reviewed relational model w/Ryan'},
		{ timestamp: '', note: 'Reviewed document model w/Ryan'}
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
	type: 'Task',
	taskId: '2',
	description: 'Finalize walkthrough doc'
}
{
	type: 'Note',
	noteId: 1,
	taskId: 2,
	timestamp: 111,
	note: 'Reviewed relational model w/Ryan'
}
```
We now have two queries if we want a task plus notes. However, if notes aren't
often viewed immediately (e.g. you merely list the Task names, and then show
notes only when a task's details are shown), this might not really impact
performance.

### Note Id's within Task

If you needed to quickly discover how many notes a particular task had (if any), you
could optionally store an array of Note Id's into the Task document.

{
	type: 'Task',
	taskId: '2',
	description: 'Finalize walkthrough doc',
	notes: [ 1, 5, 7 ]
}

### Note highlights within Task

Just like online merchants display one or two relevant product reviews on the summary
page, you may want to show the most recent note (or at least part of it). So... consider
this bit of denormalization, storing something about the most recent note (or array of most recent notes)

{
	type: 'Task',
	taskId: '2',
	description: 'Finalize walkthrough doc',
	notes: [ 1, 5, 7 ],
    mostRecentNotes: [
    	{ noteId: 1, timestamp: '', summary: "I reviewd..."}
    ]
}

### Queries: Task notes

#### Retrieve a task and all associated notes, in reverse-chrono order

TBD

# Putting it all together - our Document model

So... we have several options for our final model, each with various tradeoffs around:
 - Embed vs reference (and volatility)
 - Number of reads / writes required
 - Unbounded vs bounded
 - Denormalization

There's really no single correct way to model this data, but here's a strawman example,
based on the following decisions:
 - Task-first approach. That is, approach #2 from above, where each task references
 the person(s) working on the task.
 - Task categories referenced as an array of category ID's within a Task
 - Task notes stored as separate documents. This gives us leeway to add other 
 information to notes, and to view them separately

The way you start might not be the way you end up:
 - Imagine building a Task-first system and finding out, through telemetry,
 people are using it in a Person-first way
Putting this all together:

```
{
	type: 'Category',
	categoryId: 3,
	description: 'Work'
}
{
	type: 'Category',
	categoryId: 5,
	description: 'Indoors'
}
{
	type: 'Person',
	personId: 1,
	username: 'David',
	tasks: [ 1, 2 ]
}
{
	type: 'Person',
	personId: 2,
	username: 'Ryan',
	assistWithTasks: [ 2 ]
}
{
	type: 'Task',
	taskId: '2',
	person: { personId:1, username: 'David', assignedDate: ''},
	assistants: [ { personId: 2, username: 'Ryan', assignedDate: '' } ],
	description: 'Finalize walkthrough doc',
	categories: [
		{ categoryId: 3, categoryName: 'Work'},
		{ categoryId: 5, categoryName: 'Indoors'}
	],
	notes: [ 1, 2 ],
    mostRecentNotes: [
    	{ noteId: 1, timestamp: '', summary: "I reviewd relational model w/Ryan..."}
    ]
}
{
	type: 'Note',
	taskId: 2,
	person: { personId: 1, username: 'David'},
	Timestamp: '',
	Note: 'I reviewed relational model w/Ryan and blah blah foo bar something really long goes in to this note. Yipoeee Yay.'
}
```

### Queries: Strawman model

#### Getting tasks

Get all tasks for a given person, along with description, categories
 and status.

```
TBD
```

#### Getting the list of people with tasks assigned

First: A list of primary task owners

```
TBD
```

Now, find people who are tagged as assistants

```
TBD
```